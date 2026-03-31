document.addEventListener('DOMContentLoaded', () => {
    // Словник перекладів
    const translations = {
        uk: {
            nav_features: "Функції", nav_employees: "Працівники", nav_templates: "Шаблони", nav_finance: "Фінанси", nav_invoices: "Рахунки", nav_ai: "AI",
            hero_title: "Повне управління<br><span class='gradient-text'>кадровим агентством</span>",
            hero_subtitle: "Працівники, документи, шаблони, зарплати, рахунки, звіти — все в одній програмі з AI-інтеграцією та 4-мовною підтримкою",
            hero_btn_download: "Завантажити безкоштовно (14 днів)", hero_btn_features: "Переглянути функції",
            stat_modules: "Модулів", stat_languages: "Мови", stat_templates: "Шаблонів",
            feat_tag: "Можливості", feat_title: "Все для управління агентством", feat_subtitle: "18 модулів, що покривають кожен аспект роботи кадрового агентства",
            feat_dash_title: "Дашборд", feat_dash_desc: "Інтерактивна панель з віджетами, статистикою, AI-звітами та гнучким компонуванням",
            feat_emp_title: "Працівники", feat_emp_desc: "Повний цикл: від wizard-створення до архівації, з AI-скануванням документів",
            feat_tpl_title: "Шаблони", feat_tpl_desc: "DOCX, XLSX, PDF редактори з каталогом тегів та AI-вставкою змінних",
            feat_fin_title: "Фінанси", feat_fin_desc: "Зарплатні таблиці, аванси, витрати, експорт — помісячний облік",
            feat_rep_title: "Звіти", feat_rep_desc: "Аналітика по фірмах, агенціях, працівниках з графіками та PDF/Excel експортом",
            feat_inv_title: "Рахунки", feat_inv_desc: "Фактури, цінові пропозиції, касові ордери з QR-платежами та ARES",
            feat_ai_title: "AI-асистент", feat_ai_desc: "Вбудований чат Gemini зі спеціалізацією на кадровому праві та документах",
            feat_sec_title: "Безпека", feat_sec_desc: "Шифрування AES/DPAPI, паролі PBKDF2, підтвердження для небезпечних дій",
            sec_tag: "Конфіденційність", sec_title: "Ваші дані належать лише вам", sec_desc: "Ми не маємо доступу до вашої бази. Всі дані зберігаються локально і захищені за стандартами банківських систем (GDPR Compliant).",
            sec_f1_title: "Локальне зберігання", sec_f1_desc: "— база даних знаходиться тільки на вашому ПК. Жодних витоків у мережу.",
            sec_f2_title: "AES-256 Шифрування", sec_f2_desc: "— файли бази зашифровані. Навіть якщо хтось вкраде файл, він не зможе його прочитати.",
            sec_f3_title: "Робота з 2-х ПК (OneDrive)", sec_f3_desc: "— за бажанням, ви можете розмістити базу в папці OneDrive/Google Drive і безпечно працювати з різних комп'ютерів.",
            sec_f4_title: "Захист від видалення", sec_f4_desc: "— атомарний запис гарантує, що дані не пропадуть при раптовому вимкненні світла.",
            pricing_tag: "Тарифи", pricing_title: "Прозоре ціноутворення", pricing_subtitle: "Виберіть план, який найкраще підходить для вашого бізнесу. Без прихованих платежів.",
            price_std_title: "Стандарт", price_year: " CZK / рік", price_std_desc: "Повний функціонал для управління агентством без використання штучного інтелекту.",
            price_f_emp_unlim: "Необмежено працівників", price_f_docs: "Генерація всіх документів", price_f_fin_inv: "Фінанси та Рахунки-фактури", price_f_updates: "Безкоштовні оновлення", price_f_no_ai: "Без AI-функцій",
            price_btn_start: "Почати 14 днів безкоштовно", price_recommended: "Рекомендовано", price_ult_title: "Ultimate AI", price_ult_desc: "Максимальна автоматизація завдяки інтеграції з Google Gemini.",
            price_f_all_std: "Все з тарифу \"Стандарт\"", price_f_ai_scan: "AI-сканування документів", price_f_ai_chat: "Вбудований AI-асистент (Чат)", price_f_ai_tags: "AI-автозаповнення шаблонів", price_f_ai_reports: "Автоматичні AI-звіти",
            roi_tag: "Ефективність", roi_title: "Окупається за перший тиждень", roi_desc: "Подивіться, скільки часу ви втрачаєте на рутину щомісяця (з розрахунку на 50 працівників), і як Agency Contractor це змінює.",
            roi_without: "Без програми (Excel + Word)", roi_time1: "~35 годин", roi_tasks1: "Ручне введення даних, пошук помилок, контроль термінів",
            roi_with: "З Agency Contractor", roi_time2: "~2 години", roi_tasks2: "AI сканування, автогенерація в 1 клік, автоматичні сповіщення",
            roi_save_num: "94%", roi_save_text: "Зекономленого часу", roi_save_sub: "на кожного HR-менеджера",
            faq_title: "Поширені запитання", faq_q1: "Чи безпечні мої дані?", faq_a1: "Абсолютно. Всі дані зберігаються локально на вашому комп'ютері та зашифровані за допомогою алгоритму AES-256 та Windows DPAPI. Ми не маємо доступу до вашої бази даних.",
            faq_q2: "Чи потрібен інтернет для роботи?", faq_a2: "Основний функціонал програми працює повністю офлайн. Інтернет потрібен лише для функцій AI (Google Gemini), інтеграції з ARES (реєстр Чехії) та перевірки ліцензії.",
            faq_q3: "Як працює 14-денний тестовий період?", faq_a3: "Ви завантажуєте програму і отримуєте повний доступ до всіх функцій (рівень \"Безліміт\") на 14 днів. Жодної прив'язки картки. Після завершення періоду ви зможете обрати зручний тариф.",
            faq_q4: "Чому Windows або антивірус блокує файл?", faq_a4: "Оскільки програма нова і розробник ще не має накопиченого \"рейтингу довіри\" від Microsoft (SmartScreen), система може попередити про невідомий файл. Це абсолютно безпечно. При завантаженні дозвольте збереження файлу, а при запуску натисніть \"Докладніше\" (More info) та \"Виконати в будь-якому випадку\" (Run anyway).",
            faq_q5: "Який саме файл потрібно скачувати?", faq_a5: "На сторінці GitHub Releases завжди обирайте найновішу версію (позначка Latest). Для встановлення програми вам потрібен файл із назвою <strong>AgencyContractor-win-Setup.exe</strong>.",
            replace_tag: "Екосистема", replace_title: "Замінює хаос на систему", replace_subtitle: "Навіщо платити за 5 різних сервісів і плутатись у вікнах? Усе необхідне вже тут.", replace_all: "Все в одному місці",
            perfect_tag: "Для кого", perfect_title: "Ідеально підходить для вашого бізнесу", perfect_subtitle: "Програма адаптується під різні моделі роботи з персоналом",
            perfect_c1_title: "Кадрові агенції", perfect_c1_1: "Масовий найм та облік", perfect_c1_2: "Контроль термінів віз та дозволів", perfect_c1_3: "Розподіл по фірмах-клієнтах", perfect_c1_4: "Генерація фактур за послуги",
            perfect_c2_title: "Прямі роботодавці", perfect_c2_1: "Заводи, склади, виробництва", perfect_c2_2: "Облік годин та змін", perfect_c2_3: "Автоматизація договорів", perfect_c2_4: "Надійний локальний архів",
            perfect_c3_title: "HR та Бухгалтери", perfect_c3_1: "Зарплатні відомості без помилок", perfect_c3_2: "Швидкий розрахунок авансів", perfect_c3_3: "AI-помічник з кадрових питань", perfect_c3_4: "Експорт звітів в Excel/PDF",
            roi_emp_label: "Кількість працівників:", roi_bad_title: "Без програми (Витрати часу)", roi_hours: "год/міс", roi_bad_desc: "Ручне введення, пошук помилок, папери", roi_good_title: "З Agency Contractor", roi_good_desc: "Фіксована ціна 9 000 CZK/рік. Безліміт працівників, AI автоматизація, генерація в 1 клік.", roi_saved_text: "Економія:",
            roi_bd_docs: "📄 Документи:", roi_bd_salary: "💰 Зарплати:", roi_bd_invoices: "🧾 Фактури:", roi_bd_errors: "⚠️ Помилки:",
            marquee_1: "✨ Powered by Google Gemini AI", marquee_2: "🔒 AES-256 Шифрування", marquee_3: "☁️ OneDrive Синхронізація", marquee_4: "🏢 ARES Інтеграція", marquee_5: "⚡ Генерація в 1 клік",
            toast_1: "⚡ AI щойно згенерував 15 договорів", toast_2: "✅ Зарплати для 50 працівників розраховані за 1.2 сек", toast_3: "🔒 Резервна копія успішно створена", toast_4: "📄 Новий шаблон додано до бази", toast_5: "🔍 ARES оновив дані 3 компаній", toast_time: "щойно",
            sticky_cta_text: "Готові оптимізувати роботу?", sticky_cta_btn: "Почати безкоштовно",
            stat_1_title: "Годин зекономлено", stat_2_title: "Безпека даних", stat_3_title: "Помилок у документах", stat_4_title: "Задоволених HR"
        },
        cs: {
            nav_features: "Funkce", nav_employees: "Zaměstnanci", nav_templates: "Šablony", nav_finance: "Finance", nav_invoices: "Faktury", nav_ai: "AI",
            hero_title: "Kompletní správa<br><span class='gradient-text'>personální agentury</span>",
            hero_subtitle: "Zaměstnanci, dokumenty, šablony, platy, faktury, reporty — vše v jednom programu s AI integrací a podporou 4 jazyků",
            hero_btn_download: "Stáhnout zdarma (14 dní)", hero_btn_features: "Zobrazit funkce",
            stat_modules: "Modulů", stat_languages: "Jazyky", stat_templates: "Šablon",
            feat_tag: "Možnosti", feat_title: "Vše pro správu agentury", feat_subtitle: "18 modulů pokrývajících každý aspekt práce personální agentury",
            feat_dash_title: "Nástěnka", feat_dash_desc: "Interaktivní panel s widgety, statistikami, AI reporty a flexibilním rozložením",
            feat_emp_title: "Zaměstnanci", feat_emp_desc: "Plný cyklus: od vytvoření přes průvodce až po archivaci, s AI skenováním dokumentů",
            feat_tpl_title: "Šablony", feat_tpl_desc: "Editory DOCX, XLSX, PDF s katalogem značek a AI vkládáním proměnných",
            feat_fin_title: "Finance", feat_fin_desc: "Platové tabulky, zálohy, výdaje, export — měsíční účtování",
            feat_rep_title: "Reporty", feat_rep_desc: "Analytika pro firmy, agentury, zaměstnance s grafy a exportem do PDF/Excel",
            feat_inv_title: "Faktury", feat_inv_desc: "Faktury, cenové nabídky, pokladní doklady s QR platbami a ARES",
            feat_ai_title: "AI asistent", feat_ai_desc: "Vestavěný chat Gemini se specializací na pracovní právo a dokumenty",
            feat_sec_title: "Bezpečnost", feat_sec_desc: "Šifrování AES/DPAPI, hesla PBKDF2, potvrzení pro nebezpečné akce",
            sec_tag: "Soukromí", sec_title: "Vaše data patří jen vám", sec_desc: "Nemáme přístup k vaší databázi. Všechna data jsou uložena lokálně a chráněna podle standardů bankovních systémů (GDPR Compliant).",
            sec_f1_title: "Lokální úložiště", sec_f1_desc: "— databáze je pouze na vašem PC. Žádné úniky na internet.",
            sec_f2_title: "Šifrování AES-256", sec_f2_desc: "— soubory databáze jsou šifrovány. I když někdo soubor ukradne, nemůže ho přečíst.",
            sec_f3_title: "Práce ze 2 PC (OneDrive)", sec_f3_desc: "— volitelně můžete umístit databázi do složky OneDrive/Google Drive a bezpečně pracovat z různých počítačů.",
            sec_f4_title: "Ochrana proti smazání", sec_f4_desc: "— atomický zápis zaručuje, že se data neztratí při náhlém výpadku proudu.",
            pricing_tag: "Tarify", pricing_title: "Transparentní ceny", pricing_subtitle: "Vyberte si plán, který nejlépe vyhovuje vašemu podnikání. Bez skrytých poplatků.",
            price_std_title: "Standard", price_year: " CZK / rok", price_std_desc: "Plná funkčnost pro správu agentury bez využití umělé inteligence.",
            price_f_emp_unlim: "Neomezeně zaměstnanců", price_f_docs: "Generování všech dokumentů", price_f_fin_inv: "Finance a faktury", price_f_updates: "Aktualizace zdarma", price_f_no_ai: "Bez AI funkcí",
            price_btn_start: "Začít 14 dní zdarma", price_recommended: "Doporučeno", price_ult_title: "Ultimate AI", price_ult_desc: "Maximální automatizace díky integraci s Google Gemini.",
            price_f_all_std: "Vše z tarifu \"Standard\"", price_f_ai_scan: "AI skenování dokumentů", price_f_ai_chat: "Vestavěný AI asistent (Chat)", price_f_ai_tags: "AI automatické vyplňování šablon", price_f_ai_reports: "Automatické AI reporty",
            roi_tag: "Efektivita", roi_title: "Vrátí se za první týden", roi_desc: "Podívejte se, kolik času ztrácíte rutinou každý měsíc (počítáno na 50 zaměstnanců) a jak to Agency Contractor mění.",
            roi_without: "Bez programu (Excel + Word)", roi_time1: "~35 hodin", roi_tasks1: "Ruční zadávání dat, hledání chyb, kontrola termínů",
            roi_with: "S Agency Contractor", roi_time2: "~2 hodiny", roi_tasks2: "AI skenování, automatické generování, automatická upozornění",
            roi_save_num: "94%", roi_save_text: "Ušetřeného času", roi_save_sub: "na každého HR manažera",
            faq_title: "Často kladené otázky", faq_q1: "Jsou moje data v bezpečí?", faq_a1: "Absolutně. Všechna data jsou uložena lokálně ve vašem počítači a šifrována pomocí algoritmu AES-256 a Windows DPAPI. K vaší databázi nemáme přístup.",
            faq_q2: "Potřebuji k práci internet?", faq_a2: "Základní funkce programu fungují zcela offline. Internet je vyžadován pouze pro funkce AI (Google Gemini), integraci s ARES (český registr) a ověření licence.",
            faq_q3: "Jak funguje 14denní zkušební doba?", faq_a3: "Stáhnete si program a získáte plný přístup ke všem funkcím (úroveň \"Neomezený\") na 14 dní. Žádné vázání karty. Po uplynutí doby si můžete vybrat vhodný tarif.",
            faq_q4: "Proč Windows nebo antivirus blokuje soubor?", faq_a4: "Jelikož je program nový a vývojář ještě nemá \"hodnocení důvěryhodnosti\" od Microsoftu (SmartScreen), systém může varovat před neznámým souborem. Je to naprosto bezpečné. Při stahování povolte uložení souboru a při spuštění klikněte na \"Další informace\" (More info) a \"Přesto spustit\" (Run anyway).",
            faq_q5: "Který soubor si mám stáhnout?", faq_a5: "Na stránce GitHub Releases vždy vybírejte nejnovější verzi (označení Latest). Pro instalaci programu potřebujete soubor s názvem <strong>AgencyContractor-win-Setup.exe</strong>.",
            replace_tag: "Ekosystém", replace_title: "Mění chaos v systém", replace_subtitle: "Proč platit za 5 různých služeb a zmatkovat v oknech? Vše potřebné je už tady.", replace_all: "Vše na jednom místě",
            perfect_tag: "Pro koho", perfect_title: "Ideální pro vaše podnikání", perfect_subtitle: "Program se přizpůsobí různým modelům práce s personálem",
            perfect_c1_title: "Personální agentury", perfect_c1_1: "Hromadný nábor a evidence", perfect_c1_2: "Kontrola termínů víz a povolení", perfect_c1_3: "Rozdělení podle klientských firem", perfect_c1_4: "Generování faktur za služby",
            perfect_c2_title: "Přímí zaměstnavatelé", perfect_c2_1: "Továrny, sklady, výroba", perfect_c2_2: "Evidence hodin a směn", perfect_c2_3: "Automatizace smluv", perfect_c2_4: "Spolehlivý lokální archiv",
            perfect_c3_title: "HR a Účetní", perfect_c3_1: "Mzdové výkazy bez chyb", perfect_c3_2: "Rychlý výpočet záloh", perfect_c3_3: "AI asistent pro HR otázky", perfect_c3_4: "Export reportů do Excel/PDF",
            roi_emp_label: "Počet zaměstnanců:", roi_bad_title: "Bez programu (Ztráta času)", roi_hours: "hod/měs", roi_bad_desc: "Ruční zadávání, hledání chyb, papíry", roi_good_title: "S Agency Contractor", roi_good_desc: "Fixní cena 9 000 CZK/rok. Neomezeně zaměstnanců, AI automatizace, generování 1 kliknutím.", roi_saved_text: "Úspora:",
            roi_bd_docs: "📄 Dokumenty:", roi_bd_salary: "💰 Platy:", roi_bd_invoices: "🧾 Faktury:", roi_bd_errors: "⚠️ Chyby:",
            marquee_1: "✨ Powered by Google Gemini AI", marquee_2: "🔒 AES-256 Šifrování", marquee_3: "☁️ OneDrive Synchronizace", marquee_4: "🏢 ARES Integrace", marquee_5: "⚡ Generování 1 kliknutím",
            toast_1: "⚡ AI právě vygenerovala 15 smluv", toast_2: "✅ Platy pro 50 zaměstnanců vypočítány za 1.2 s", toast_3: "🔒 Záloha byla úspěšně vytvořena", toast_4: "📄 Nová šablona přidána do databáze", toast_5: "🔍 ARES aktualizoval data 3 firem", toast_time: "právě teď",
            sticky_cta_text: "Připraveni optimalizovat práci?", sticky_cta_btn: "Začít zdarma",
            stat_1_title: "Hodin ušetřeno", stat_2_title: "Bezpečnost dat", stat_3_title: "Chyb v dokumentech", stat_4_title: "Spokojených HR"
        },
        en: {
            nav_features: "Features", nav_employees: "Employees", nav_templates: "Templates", nav_finance: "Finance", nav_invoices: "Invoices", nav_ai: "AI",
            hero_title: "Complete management<br><span class='gradient-text'>for staffing agencies</span>",
            hero_subtitle: "Employees, documents, templates, salaries, invoices, reports — all in one app with AI integration and 4-language support",
            hero_btn_download: "Download Free (14 days)", hero_btn_features: "View Features",
            stat_modules: "Modules", stat_languages: "Languages", stat_templates: "Templates",
            feat_tag: "Capabilities", feat_title: "Everything for agency management", feat_subtitle: "18 modules covering every aspect of a staffing agency's work",
            feat_dash_title: "Dashboard", feat_dash_desc: "Interactive panel with widgets, statistics, AI reports, and flexible layout",
            feat_emp_title: "Employees", feat_emp_desc: "Full cycle: from wizard creation to archiving, with AI document scanning",
            feat_tpl_title: "Templates", feat_tpl_desc: "DOCX, XLSX, PDF editors with tag catalog and AI variable insertion",
            feat_fin_title: "Finance", feat_fin_desc: "Salary tables, advances, expenses, export — monthly accounting",
            feat_rep_title: "Reports", feat_rep_desc: "Analytics for companies, agencies, employees with charts and PDF/Excel export",
            feat_inv_title: "Invoices", feat_inv_desc: "Invoices, quotes, cash receipts with QR payments and ARES",
            feat_ai_title: "AI Assistant", feat_ai_desc: "Built-in Gemini chat specializing in labor law and documents",
            feat_sec_title: "Security", feat_sec_desc: "AES/DPAPI encryption, PBKDF2 passwords, confirmation for dangerous actions",
            sec_tag: "Privacy", sec_title: "Your data belongs only to you", sec_desc: "We do not have access to your database. All data is stored locally and protected by banking system standards (GDPR Compliant).",
            sec_f1_title: "Local Storage", sec_f1_desc: "— the database is only on your PC. No leaks to the internet.",
            sec_f2_title: "AES-256 Encryption", sec_f2_desc: "— database files are encrypted. Even if someone steals the file, they cannot read it.",
            sec_f3_title: "Work from 2 PCs (OneDrive)", sec_f3_desc: "— optionally, you can place the database in a OneDrive/Google Drive folder and work safely from different computers.",
            sec_f4_title: "Deletion Protection", sec_f4_desc: "— atomic write guarantees that data will not be lost during a sudden power outage.",
            pricing_tag: "Pricing", pricing_title: "Transparent Pricing", pricing_subtitle: "Choose the plan that best fits your business. No hidden fees.",
            price_std_title: "Standard", price_year: " CZK / year", price_std_desc: "Full functionality for agency management without using artificial intelligence.",
            price_f_emp_unlim: "Unlimited employees", price_f_docs: "Generation of all documents", price_f_fin_inv: "Finance and Invoices", price_f_updates: "Free updates", price_f_no_ai: "No AI features",
            price_btn_start: "Start 14 days free", price_recommended: "Recommended", price_ult_title: "Ultimate AI", price_ult_desc: "Maximum automation thanks to integration with Google Gemini.",
            price_f_all_std: "Everything from \"Standard\" plan", price_f_ai_scan: "AI document scanning", price_f_ai_chat: "Built-in AI assistant (Chat)", price_f_ai_tags: "AI auto-filling templates", price_f_ai_reports: "Automatic AI reports",
            roi_tag: "Efficiency", roi_title: "Pays off in the first week", roi_desc: "See how much time you lose on routine every month (based on 50 employees), and how Agency Contractor changes it.",
            roi_without: "Without app (Excel + Word)", roi_time1: "~35 hours", roi_tasks1: "Manual data entry, finding errors, deadline control",
            roi_with: "With Agency Contractor", roi_time2: "~2 hours", roi_tasks2: "AI scanning, 1-click generation, automatic notifications",
            roi_save_num: "94%", roi_save_text: "Time Saved", roi_save_sub: "for every HR manager",
            faq_title: "Frequently Asked Questions", faq_q1: "Are my data safe?", faq_a1: "Absolutely. All data is stored locally on your computer and encrypted using AES-256 algorithm and Windows DPAPI. We do not have access to your database.",
            faq_q2: "Do I need internet to work?", faq_a2: "The core functionality of the program works completely offline. Internet is only required for AI features (Google Gemini), integration with ARES (Czech registry), and license verification.",
            faq_q3: "How does the 14-day trial work?", faq_a3: "You download the program and get full access to all features (\"Unlimited\" tier) for 14 days. No credit card required. After the period ends, you can choose a suitable plan.",
            faq_q4: "Why does Windows or antivirus block the file?", faq_a4: "Since the program is new and the developer does not yet have a \"trust rating\" from Microsoft (SmartScreen), the system may warn about an unknown file. It is completely safe. Allow the file to be saved during download, and when running, click \"More info\" and \"Run anyway\".",
            faq_q5: "Which file exactly should I download?", faq_a5: "On the GitHub Releases page, always choose the latest version (Latest tag). To install the program, you need the file named <strong>AgencyContractor-win-Setup.exe</strong>.",
            replace_tag: "Ecosystem", replace_title: "Replaces chaos with system", replace_subtitle: "Why pay for 5 different services and get lost in windows? Everything you need is already here.", replace_all: "All in one place",
            perfect_tag: "For Whom", perfect_title: "Perfect for your business", perfect_subtitle: "The program adapts to different models of personnel management",
            perfect_c1_title: "Staffing Agencies", perfect_c1_1: "Mass hiring and accounting", perfect_c1_2: "Visa and permit deadline control", perfect_c1_3: "Distribution by client companies", perfect_c1_4: "Invoice generation for services",
            perfect_c2_title: "Direct Employers", perfect_c2_1: "Factories, warehouses, production", perfect_c2_2: "Hours and shifts tracking", perfect_c2_3: "Contract automation", perfect_c2_4: "Reliable local archive",
            perfect_c3_title: "HR and Accountants", perfect_c3_1: "Error-free payrolls", perfect_c3_2: "Quick advance calculations", perfect_c3_3: "AI assistant for HR questions", perfect_c3_4: "Export reports to Excel/PDF",
            roi_emp_label: "Number of employees:", roi_bad_title: "Without app (Time lost)", roi_hours: "hrs/mo", roi_bad_desc: "Manual entry, finding errors, papers", roi_good_title: "With Agency Contractor", roi_good_desc: "Fixed price 9,000 CZK/year. Unlimited employees, AI automation, 1-click generation.", roi_saved_text: "Savings:",
            roi_bd_docs: "📄 Documents:", roi_bd_salary: "💰 Salaries:", roi_bd_invoices: "🧾 Invoices:", roi_bd_errors: "⚠️ Errors:",
            marquee_1: "✨ Powered by Google Gemini AI", marquee_2: "🔒 AES-256 Encryption", marquee_3: "☁️ OneDrive Sync", marquee_4: "🏢 ARES Integration", marquee_5: "⚡ 1-Click Generation",
            toast_1: "⚡ AI just generated 15 contracts", toast_2: "✅ Salaries for 50 employees calculated in 1.2s", toast_3: "🔒 Backup successfully created", toast_4: "📄 New template added to database", toast_5: "🔍 ARES updated data for 3 companies", toast_time: "just now",
            sticky_cta_text: "Ready to optimize your work?", sticky_cta_btn: "Start for free",
            stat_1_title: "Hours saved", stat_2_title: "Data security", stat_3_title: "Document errors", stat_4_title: "Happy HRs"
        }
    };

    // Логіка перемикання мов
    document.querySelectorAll('.lang-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.lang-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            
            const lang = btn.getAttribute('data-lang');
            document.documentElement.lang = lang;
            
            // Оновлення SEO мета-тегів при перемиканні мови
            if (lang === 'uk') {
                document.title = "Agency Contractor — Програма для управління кадровим агентством";
                document.querySelector('meta[name="description"]').setAttribute("content", "Комплексна програма для кадрових агентств та роботодавців. Облік працівників, генерація документів, зарплати, фактури та AI-асистент. Завантажте безкоштовно!");
                document.querySelector('meta[property="og:title"]').setAttribute("content", "Agency Contractor — Управління кадровим агентством");
                document.querySelector('meta[property="og:description"]').setAttribute("content", "Все для управління агентством: працівники, документи, фінанси, AI. Завантажте безкоштовно на 14 днів.");
            } else if (lang === 'cs') {
                document.title = "Agency Contractor — Software pro personální agentury";
                document.querySelector('meta[name="description"]').setAttribute("content", "Komplexní program pro personální agentury a zaměstnavatele. Evidence zaměstnanců, generování dokumentů, platy, faktury a AI asistent. Stáhněte si zdarma!");
                document.querySelector('meta[property="og:title"]').setAttribute("content", "Agency Contractor — Správa personální agentury");
                document.querySelector('meta[property="og:description"]').setAttribute("content", "Vše pro správu agentury: zaměstnanci, dokumenty, finance, AI. Stáhněte si zdarma na 14 dní.");
            } else if (lang === 'en') {
                document.title = "Agency Contractor — Staffing Agency Management Software";
                document.querySelector('meta[name="description"]').setAttribute("content", "Comprehensive software for staffing agencies and employers. Employee tracking, document generation, payroll, invoices, and AI assistant. Download for free!");
                document.querySelector('meta[property="og:title"]').setAttribute("content", "Agency Contractor — Staffing Agency Management");
                document.querySelector('meta[property="og:description"]').setAttribute("content", "Everything for agency management: employees, documents, finance, AI. Download free for 14 days.");
            }
            
            document.querySelectorAll('[data-i18n]').forEach(el => {
                const key = el.getAttribute('data-i18n');
                if (translations[lang] && translations[lang][key]) {
                    el.innerHTML = translations[lang][key];
                }
            });
        });
    });

    const navbar = document.getElementById('navbar');
    const navToggle = document.getElementById('navToggle');
    const navLinks = document.getElementById('navLinks');

    window.addEventListener('scroll', () => {
        navbar.classList.toggle('scrolled', window.scrollY > 50);
    });

    navToggle?.addEventListener('click', () => {
        navLinks.classList.toggle('open');
    });

    navLinks?.querySelectorAll('a').forEach(a => {
        a.addEventListener('click', () => navLinks.classList.remove('open'));
    });

    // ROI Calculator Logic
    const roiSlider = document.getElementById('roi-slider');
    const empCount = document.getElementById('emp-count');
    const roiTimeBad = document.getElementById('roi-time-bad');
    const roiMoneyBad = document.getElementById('roi-money-bad');
    const roiTimeGood = document.getElementById('roi-time-good');
    const roiMoneySaved = document.getElementById('roi-money-saved');
    
    // Breakdown elements
    const roiDocs = document.getElementById('roi-docs');
    const roiSalary = document.getElementById('roi-salary');
    const roiInvoices = document.getElementById('roi-invoices');
    const roiErrors = document.getElementById('roi-errors');

    function updateROI() {
        if (!roiSlider) return;
        const count = parseInt(roiSlider.value);
        empCount.textContent = count;
        
        // Formulas
        // Without app (breakdown per employee per month)
        const hoursDocs = Math.round(count * 0.5); // 30 mins per employee for docs
        const hoursSalary = Math.round(count * 0.3); // 18 mins for salary
        const hoursInvoices = Math.round(count * 0.2); // 12 mins for invoices
        const hoursErrors = Math.round(count * 0.2); // 12 mins fixing errors
        
        const totalHoursBad = hoursDocs + hoursSalary + hoursInvoices + hoursErrors;
        
        // With app (highly automated)
        const totalHoursGood = Math.round(count * 0.14); // ~8.5 mins per employee total
        
        const savedHours = totalHoursBad - totalHoursGood;

        // Current language
        const lang = document.documentElement.lang || 'uk';
        const hoursText = translations[lang]?.roi_hours || 'год/міс';
        const savedText = translations[lang]?.roi_saved_text || 'Економія:';

        // Update DOM
        roiTimeBad.textContent = `${totalHoursBad} ${hoursText}`;
        
        if(roiDocs) roiDocs.textContent = `${hoursDocs} год`;
        if(roiSalary) roiSalary.textContent = `${hoursSalary} год`;
        if(roiInvoices) roiInvoices.textContent = `${hoursInvoices} год`;
        if(roiErrors) roiErrors.textContent = `${hoursErrors} год`;
        
        roiTimeGood.textContent = `∞`; // Unlimited/Fixed
        roiTimeGood.style.fontSize = "28px";
        roiTimeGood.style.lineHeight = "1";
        roiMoneySaved.textContent = `${savedText} ${savedHours} ${hoursText}`;
    }

    if (roiSlider) {
        roiSlider.addEventListener('input', updateROI);
        // Update on language change too
        document.querySelectorAll('.lang-btn').forEach(btn => {
            btn.addEventListener('click', () => setTimeout(updateROI, 50));
        });
        updateROI();
    }

    // Glow Cursor
    const cursorGlow = document.getElementById('cursor-glow');
    if (cursorGlow) {
        document.addEventListener('mousemove', (e) => {
            cursorGlow.style.left = e.clientX + 'px';
            cursorGlow.style.top = e.clientY + 'px';
        });
    }

    // 3D Tilt Effect
    const tiltElements = document.querySelectorAll('.tilt-effect');
    tiltElements.forEach(el => {
        el.addEventListener('mousemove', (e) => {
            const rect = el.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;
            const centerX = rect.width / 2;
            const centerY = rect.height / 2;
            const rotateX = ((y - centerY) / centerY) * -5; // Max 5 deg
            const rotateY = ((x - centerX) / centerX) * 5;
            el.style.transform = `perspective(1000px) rotateX(${rotateX}deg) rotateY(${rotateY}deg)`;
        });
        el.addEventListener('mouseleave', () => {
            el.style.transform = `perspective(1000px) rotateX(0deg) rotateY(0deg)`;
        });
    });

    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('visible');
                observer.unobserve(entry.target);
            }
        });
    }, { threshold: 0.15, rootMargin: '0px 0px -40px 0px' });

    document.querySelectorAll('.animate-fade-up, .animate-slide-left, .animate-slide-right').forEach(el => {
        if (!el.closest('.hero')) observer.observe(el);
    });

    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', e => {
            e.preventDefault();
            const target = document.querySelector(anchor.getAttribute('href'));
            if (target) {
                const offset = navbar.offsetHeight + 20;
                const top = target.getBoundingClientRect().top + window.pageYOffset - offset;
                window.scrollTo({ top, behavior: 'smooth' });
            }
        });
    });

    // --- WOW Effects Logic ---

    // 1. Live Toasts
    const toastContainer = document.getElementById('toast-container');
    if (toastContainer) {
        const toastKeys = ['toast_1', 'toast_2', 'toast_3', 'toast_4', 'toast_5'];
        let lastToastIndex = -1;

        function showRandomToast() {
            let index;
            do {
                index = Math.floor(Math.random() * toastKeys.length);
            } while (index === lastToastIndex);
            lastToastIndex = index;

            const lang = document.documentElement.lang || 'uk';
            const text = translations[lang] ? translations[lang][toastKeys[index]] : "Activity";
            const timeText = translations[lang] ? translations[lang]['toast_time'] : "now";

            const toast = document.createElement('div');
            toast.className = 'live-toast';
            toast.innerHTML = `
                <div class="toast-icon">
                    <svg viewBox="0 0 24 24" width="16" height="16" stroke="var(--primary)" stroke-width="2" fill="none"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>
                </div>
                <div class="toast-text">${text}</div>
                <div class="toast-time">${timeText}</div>
            `;

            toastContainer.appendChild(toast);
            
            // Trigger reflow
            void toast.offsetWidth;
            toast.classList.add('show');

            setTimeout(() => {
                toast.classList.remove('show');
                setTimeout(() => toast.remove(), 500);
            }, 5000);
        }

        // Start showing toasts after 3 seconds, then every 8-15 seconds
        setTimeout(() => {
            showRandomToast();
            setInterval(showRandomToast, 12000);
        }, 3000);
    }

    // 2. Sticky CTA
    const stickyCta = document.getElementById('sticky-cta');
    if (stickyCta) {
        window.addEventListener('scroll', () => {
            if (window.scrollY > 600) {
                stickyCta.classList.add('visible');
            } else {
                stickyCta.classList.remove('visible');
            }
        });
    }

    // 3. Animated Counters
    const counters = document.querySelectorAll('.stat-counter');
    const counterObserver = new IntersectionObserver((entries, obs) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                const target = parseInt(entry.target.getAttribute('data-target'));
                const duration = 2000; // ms
                const step = target / (duration / 16); // 60fps
                let current = 0;

                const updateCounter = () => {
                    current += step;
                    if (current < target) {
                        entry.target.textContent = Math.ceil(current).toLocaleString();
                        requestAnimationFrame(updateCounter);
                    } else {
                        entry.target.textContent = target.toLocaleString() + (entry.target.hasAttribute('data-plus') ? '+' : (entry.target.hasAttribute('data-percent') ? '%' : ''));
                    }
                };
                
                updateCounter();
                obs.unobserve(entry.target);
            }
        });
    }, { threshold: 0.5 });

    counters.forEach(counter => counterObserver.observe(counter));

});
