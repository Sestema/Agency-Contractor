using Win11DesktopApp.Services;

namespace Win11DesktopApp
{
    public class ToastServiceLocator
    {
        public static ToastService Instance => ToastService.Instance;
    }
}
