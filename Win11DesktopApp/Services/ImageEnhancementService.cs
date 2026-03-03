using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace Win11DesktopApp.Services
{
    public class ImageEnhancementService
    {
        public Mat LoadImage(string path)
        {
            return Cv2.ImRead(path, ImreadModes.Color);
        }

        public void SaveImage(Mat image, string outputPath)
        {
            Cv2.ImWrite(outputPath, image);
        }

        public Mat AdjustBrightnessContrast(Mat src, int brightness, int contrast)
        {
            double alpha = (contrast + 100) / 100.0;
            double beta = brightness;

            var result = new Mat();
            src.ConvertTo(result, -1, alpha, beta);
            return result;
        }

        public Mat Sharpen(Mat src, double amount = 1.5)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(src, blurred, new Size(0, 0), 3);
            var result = new Mat();
            Cv2.AddWeighted(src, 1.0 + amount, blurred, -amount, 0, result);
            return result;
        }

        public Mat Deskew(Mat src, double angleDegrees)
        {
            if (Math.Abs(angleDegrees) < 0.01)
                return src.Clone();

            var center = new Point2f(src.Cols / 2f, src.Rows / 2f);
            using var rotMatrix = Cv2.GetRotationMatrix2D(center, angleDegrees, 1.0);

            double cos = Math.Abs(rotMatrix.At<double>(0, 0));
            double sin = Math.Abs(rotMatrix.At<double>(0, 1));
            int newW = (int)(src.Rows * sin + src.Cols * cos);
            int newH = (int)(src.Rows * cos + src.Cols * sin);

            rotMatrix.Set(0, 2, rotMatrix.At<double>(0, 2) + (newW - src.Cols) / 2.0);
            rotMatrix.Set(1, 2, rotMatrix.At<double>(1, 2) + (newH - src.Rows) / 2.0);

            var result = new Mat();
            Cv2.WarpAffine(src, result, rotMatrix, new Size(newW, newH),
                InterpolationFlags.Cubic, BorderTypes.Constant, Scalar.White);
            return result;
        }

        public Mat Rotate90(Mat src, bool clockwise)
        {
            var result = new Mat();
            if (clockwise)
                Cv2.Rotate(src, result, RotateFlags.Rotate90Clockwise);
            else
                Cv2.Rotate(src, result, RotateFlags.Rotate90Counterclockwise);
            return result;
        }

        public Mat CropRegion(Mat src, int x, int y, int width, int height)
        {
            x = Math.Max(0, Math.Min(x, src.Cols - 1));
            y = Math.Max(0, Math.Min(y, src.Rows - 1));
            width = Math.Max(1, Math.Min(width, src.Cols - x));
            height = Math.Max(1, Math.Min(height, src.Rows - y));

            var rect = new Rect(x, y, width, height);
            using var roi = new Mat(src, rect);
            return roi.Clone();
        }

        /// <summary>
        /// CLAHE + denoise + auto white balance in one pass.
        /// </summary>
        public Mat AutoEnhance(Mat src)
        {
            using var lab = new Mat();
            Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
            var channels = lab.Split();
            try
            {
                using var clahe = Cv2.CreateCLAHE(clipLimit: 2.5, tileGridSize: new Size(8, 8));
                clahe.Apply(channels[0], channels[0]);

                using var merged = new Mat();
                Cv2.Merge(channels, merged);
                using var enhanced = new Mat();
                Cv2.CvtColor(merged, enhanced, ColorConversionCodes.Lab2BGR);

                using var denoised = new Mat();
                Cv2.FastNlMeansDenoisingColored(enhanced, denoised, 6, 6, 7, 21);

                return AutoWhiteBalance(denoised);
            }
            finally
            {
                foreach (var ch in channels) ch.Dispose();
            }
        }

        /// <summary>
        /// Gray-world auto white balance.
        /// </summary>
        public Mat AutoWhiteBalance(Mat src)
        {
            var mean = Cv2.Mean(src);
            double avgAll = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            if (avgAll < 1) return src.Clone();

            double scaleB = avgAll / Math.Max(mean.Val0, 1);
            double scaleG = avgAll / Math.Max(mean.Val1, 1);
            double scaleR = avgAll / Math.Max(mean.Val2, 1);

            var channels = src.Split();
            try
            {
                channels[0].ConvertTo(channels[0], -1, scaleB, 0);
                channels[1].ConvertTo(channels[1], -1, scaleG, 0);
                channels[2].ConvertTo(channels[2], -1, scaleR, 0);

                var result = new Mat();
                Cv2.Merge(channels, result);
                return result;
            }
            finally
            {
                foreach (var ch in channels) ch.Dispose();
            }
        }

        /// <summary>
        /// Colored denoising with adjustable strength (0–40).
        /// </summary>
        public Mat Denoise(Mat src, int strength)
        {
            if (strength <= 0) return src.Clone();
            strength = Math.Min(strength, 40);
            var result = new Mat();
            Cv2.FastNlMeansDenoisingColored(src, result, strength, strength, 7, 21);
            return result;
        }

        /// <summary>
        /// Gamma correction: gamma &lt; 1 brightens, &gt; 1 darkens.
        /// </summary>
        public Mat GammaCorrection(Mat src, double gamma)
        {
            if (Math.Abs(gamma - 1.0) < 0.01)
                return src.Clone();

            gamma = Math.Max(0.1, Math.Min(gamma, 3.0));
            double invGamma = 1.0 / gamma;

            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
                lut[i] = (byte)Math.Min(255, Math.Max(0, Math.Pow(i / 255.0, invGamma) * 255.0));

            using var lutMat = Mat.FromArray(lut);
            var result = new Mat();
            Cv2.LUT(src, lutMat, result);
            return result;
        }

        /// <summary>
        /// Robust perspective correction using multiple detection strategies.
        /// </summary>
        public Mat? PerspectiveCorrect(Mat src)
        {
            var quad = FindDocumentQuad(src);
            if (quad == null) return null;
            return WarpQuad(src, quad);
        }

        private OpenCvSharp.Point[]? FindDocumentQuad(Mat src)
        {
            double scale = 1.0;
            Mat work = src;
            bool scaled = false;
            if (src.Cols > 1200)
            {
                scale = 1200.0 / src.Cols;
                work = new Mat();
                Cv2.Resize(src, work, new Size(0, 0), scale, scale);
                scaled = true;
            }

            try
            {
                using var gray = new Mat();
                Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);

                var quad = TryCanny(gray, work) ?? TryAdaptive(gray, work) ?? TryMorphology(gray, work);
                if (quad == null) return null;

                if (Math.Abs(scale - 1.0) > 0.01)
                {
                    double inv = 1.0 / scale;
                    for (int i = 0; i < quad.Length; i++)
                        quad[i] = new OpenCvSharp.Point((int)(quad[i].X * inv), (int)(quad[i].Y * inv));
                }
                return quad;
            }
            finally
            {
                if (scaled) work.Dispose();
            }
        }

        /// <summary>
        /// Strategy 1: Adaptive Canny thresholds based on image median.
        /// </summary>
        private OpenCvSharp.Point[]? TryCanny(Mat gray, Mat src)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            double median = GetMedian(blurred);
            double lo = Math.Max(10, median * 0.5);
            double hi = Math.Min(250, median * 1.5);

            using var edges = new Mat();
            Cv2.Canny(blurred, edges, lo, hi);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel);
            Cv2.Dilate(edges, edges, kernel);

            return FindBestQuad(edges, src, 0.05, new[] { 0.02, 0.035, 0.05 });
        }

        /// <summary>
        /// Strategy 2: Adaptive thresholding for low-contrast images.
        /// </summary>
        private OpenCvSharp.Point[]? TryAdaptive(Mat gray, Mat src)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

            using var thresh = new Mat();
            Cv2.AdaptiveThreshold(blurred, thresh, 255,
                AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 15, 4);
            Cv2.BitwiseNot(thresh, thresh);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7));
            Cv2.MorphologyEx(thresh, thresh, MorphTypes.Close, kernel);

            return FindBestQuad(thresh, src, 0.05, new[] { 0.03, 0.05, 0.08 });
        }

        /// <summary>
        /// Strategy 3: Strong morphological operations for noisy backgrounds.
        /// </summary>
        private OpenCvSharp.Point[]? TryMorphology(Mat gray, Mat src)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(7, 7), 0);

            using var binary = new Mat();
            Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            using var kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 15));
            Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernelClose);
            using var kernelOpen = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernelOpen);

            using var edges = new Mat();
            Cv2.Canny(binary, edges, 30, 100);

            using var dilateKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.Dilate(edges, edges, dilateKernel, iterations: 2);

            return FindBestQuad(edges, src, 0.03, new[] { 0.03, 0.05, 0.08, 0.1 });
        }

        private OpenCvSharp.Point[]? FindBestQuad(Mat edgeMask, Mat src, double minAreaRatio, double[] epsilonFactors)
        {
            Cv2.FindContours(edgeMask, out OpenCvSharp.Point[][] contours, out _,
                RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0) return null;

            double imgArea = src.Rows * src.Cols;
            double minArea = imgArea * minAreaRatio;
            OpenCvSharp.Point[]? bestQuad = null;
            double bestArea = 0;

            foreach (var contour in contours.OrderByDescending(c => Cv2.ContourArea(c)).Take(20))
            {
                double area = Cv2.ContourArea(contour);
                if (area < minArea) continue;
                if (area > imgArea * 0.98) continue;

                double peri = Cv2.ArcLength(contour, true);
                foreach (double eps in epsilonFactors)
                {
                    var approx = Cv2.ApproxPolyDP(contour, eps * peri, true);
                    if (approx.Length == 4 && Cv2.IsContourConvex(approx) && area > bestArea)
                    {
                        bestQuad = approx;
                        bestArea = area;
                        break;
                    }
                }
            }
            return bestQuad;
        }

        private Mat WarpQuad(Mat src, OpenCvSharp.Point[] quad)
        {
            var pts = OrderCorners(quad);
            double wTop = Distance(pts[0], pts[1]);
            double wBot = Distance(pts[3], pts[2]);
            double hLeft = Distance(pts[0], pts[3]);
            double hRight = Distance(pts[1], pts[2]);
            int maxW = (int)Math.Max(wTop, wBot);
            int maxH = (int)Math.Max(hLeft, hRight);

            if (maxW < 50 || maxH < 50) return src.Clone();

            var srcPts = pts.Select(p => new Point2f(p.X, p.Y)).ToArray();
            var dstPts = new Point2f[] {
                new(0, 0), new(maxW - 1, 0),
                new(maxW - 1, maxH - 1), new(0, maxH - 1)
            };

            using var transform = Cv2.GetPerspectiveTransform(srcPts, dstPts);
            var result = new Mat();
            Cv2.WarpPerspective(src, result, transform, new Size(maxW, maxH),
                InterpolationFlags.Cubic, BorderTypes.Constant, Scalar.White);
            return result;
        }

        private static double GetMedian(Mat gray)
        {
            using var hist = new Mat();
            Cv2.CalcHist(new[] { gray }, new[] { 0 }, null, hist,
                1, new[] { 256 }, new[] { new Rangef(0, 256) });
            int total = gray.Rows * gray.Cols;
            int half = total / 2;
            int acc = 0;
            for (int i = 0; i < 256; i++)
            {
                acc += (int)hist.At<float>(i);
                if (acc >= half) return i;
            }
            return 128;
        }

        private static OpenCvSharp.Point[] OrderCorners(OpenCvSharp.Point[] pts)
        {
            var center = new OpenCvSharp.Point(
                (int)pts.Average(p => p.X),
                (int)pts.Average(p => p.Y));

            var tl = pts.OrderBy(p => Distance(p, new OpenCvSharp.Point(0, 0))).First();
            var tr = pts.OrderBy(p => Distance(p, new OpenCvSharp.Point(int.MaxValue / 2, 0))).First();
            var bl = pts.OrderBy(p => Distance(p, new OpenCvSharp.Point(0, int.MaxValue / 2))).First();
            var br = pts.OrderBy(p => Distance(p, new OpenCvSharp.Point(int.MaxValue / 2, int.MaxValue / 2))).First();

            var sorted = pts.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X)).ToArray();
            tl = sorted.OrderBy(p => p.X + p.Y).First();
            br = sorted.OrderByDescending(p => p.X + p.Y).First();
            tr = sorted.OrderBy(p => p.Y - p.X).First();
            bl = sorted.OrderByDescending(p => p.Y - p.X).First();

            return new[] { tl, tr, br, bl };
        }

        private static double Distance(OpenCvSharp.Point a, OpenCvSharp.Point b)
        {
            return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        }
    }
}
