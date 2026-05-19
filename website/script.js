document.addEventListener('DOMContentLoaded', () => {
    // Словник перекладів
    const translations = {
        uk: {
            nav_features: "Функції", nav_employees: "Працівники", nav_templates: "Шаблони", nav_finance: "Фінанси", nav_invoices: "Рахунки", nav_ai: "AI",
            hero_title: "Працівники, документи, зарплати та AI<br><span class='gradient-text'>в одній системі</span>",
            hero_subtitle: "Agency Contractor замінює Excel, Word, папки та ручний контроль термінів єдиною програмою для кадрових агентств, роботодавців, HR та бухгалтерії.",
            hero_btn_download: "Завантажити безкоштовно (14 днів)", hero_btn_features: "Переглянути функції",
            hero_proof_1: "Локальна або Мережева БД", hero_proof_2: "Telegram-бот керівника", hero_proof_3: "14 днів безкоштовно",
            stat_modules: "Модулів", stat_languages: "Мови", stat_templates: "Шаблонів",
            outcome_tag: "Результат", outcome_title: "Замість хаосу — контроль над кожним процесом", outcome_subtitle: "Програма не просто зберігає дані. Вона показує, що потрібно зробити, де є ризики і які цифри важливі сьогодні.",
            outcome_1_title: "Менше ручної роботи", outcome_1_desc: "AI-сканування документів, шаблони, автозаповнення і масова генерація прибирають повторні дії.",
            outcome_2_title: "Контроль документів", outcome_2_desc: "Паспорт, віза, страховка, дозвіл на роботу і власні документи контролюються автоматично.",
            outcome_3_title: "Фінанси без плутанини", outcome_3_desc: "Зарплати, аванси, борги, витрати фірм, PDF/Excel експорти та підсумки за місяць в одному місці.",
            outcome_4_title: "Керування з телефону", outcome_4_desc: "Telegram-бот відповідає на питання по працівниках, зарплатах, документах і аналітиці голосом або текстом.",
            chaos_1: "Документи", chaos_2: "Працівники", chaos_3: "Зарплати", chaos_4: "AI + Telegram",
            feat_tag: "Можливості", feat_title: "Все для управління агентством", feat_subtitle: "18 модулів, що покривають кожен аспект роботи кадрового агентства",
            feat_dash_title: "Дашборд", feat_dash_desc: "Інтерактивна панель з віджетами, статистикою, AI-звітами та гнучким компонуванням",
            feat_emp_title: "Працівники", feat_emp_desc: "Повний цикл: від wizard-створення до архівації, з AI-скануванням документів",
            feat_tpl_title: "Шаблони", feat_tpl_desc: "DOCX, XLSX, PDF редактори з каталогом тегів та AI-вставкою змінних",
            feat_fin_title: "Фінанси", feat_fin_desc: "Зарплатні таблиці, аванси, витрати, експорт — помісячний облік",
            feat_rep_title: "Звіти", feat_rep_desc: "Аналітика по фірмах, агенціях, працівниках з графіками та PDF/Excel експортом",
            feat_inv_title: "Рахунки", feat_inv_desc: "Фактури, цінові пропозиції, касові ордери з QR-платежами та ARES",
            feat_ai_title: "AI-асистент", feat_ai_desc: "Вбудований чат Gemini зі спеціалізацією на кадровому праві та документах",
            feat_sec_title: "Безпека", feat_sec_desc: "Шифрування AES/DPAPI, паролі PBKDF2, підтвердження для небезпечних дій",
            feat_tg_title: "Telegram-бот", feat_tg_desc: "Управління агентством з телефону: пошук, зарплати, аналітика та голосовий AI-помічник",
            tg_tag: "Мобільність", tg_title: "Telegram-бот керівника", tg_desc: "Миттєвий доступ до всієї бази агентства прямо з вашого телефону — без необхідності відкривати комп'ютер.",
            tg_f1: "<strong>Голосовий AI-пошук</strong> — просто скажіть боту голосове повідомлення, він розпізнає мову і знайде працівника, фірму або звіт.",
            tg_f2: "<strong>Розумний контекст</strong> — бот пам'ятає розмову. Запитайте \"Знайди Олега\", а потім просто напишіть \"А які в нього аванси?\".",
            tg_f3: "<strong>Фінанси в кишені</strong> — зарплати за будь-який місяць, зведення по фірмах, порівняння місяців (дельта) та топ виплат.",
            tg_f4: "<strong>Контроль документів</strong> — швидкий запит списку документів, що закінчуються через 7 або 30 днів.",
            tg_f5: "<strong>Аналітика кадрів</strong> — статистика прийнятих/звільнених за місяць та чистий баланс руху кадрів.",
            tg_f6: "<strong>HR Новини</strong> — отримуйте актуальні оновлення про міграцію та законодавство прямо в чат.",
            sec_tag: "Архітектура", sec_title: "Гнучка архітектура та Командна робота", sec_desc: "Програма адаптується під масштаби вашого бізнесу: від автономної роботи на одному комп'ютері до повноцінної мережевої системи для всієї команди.",
            sec_f1_title: "Для одного ПК (SQLite)", sec_f1_desc: "— працюйте максимально швидко з локальною базою даних. Усі дані зберігаються виключно на вашому комп'ютері.",
            sec_f2_title: "Синхронізація (OneDrive)", sec_f2_desc: "— розмістіть файл бази та документи у хмарному сховищі, щоб мати доступ до робочого простору з різних ПК по черзі.",
            sec_f3_title: "Командна робота (PostgreSQL)", sec_f3_desc: "— об'єднайте 3–5+ користувачів. Розгорніть базу на головному ПК або сервері та підключайте інших співробітників через LAN або Tailscale.",
            sec_f4_title: "Live Sync (Миттєва синхронізація)", sec_f4_desc: "— при роботі через PostgreSQL усі зміни між ПК передаються миттєво. Інші користувачі бачать оновлення без затримок.",
            
            more_tag: "Додатково", more_title: "Більше ніж просто база даних", more_subtitle: "Ще 9 функцій, які роблять вашу щоденну роботу швидшою та приємнішою",
            mf_1_title: "Редактор зображень", mf_1_desc: "Обрізка, поворот та стиснення фотографій і сканів прямо в програмі.",
            mf_2_title: "Авто-бекапи", mf_2_desc: "Створення резервних копій перед важливими змінами. Відкат в 1 клік.",
            mf_3_title: "Багатомовність", mf_3_desc: "Інтерфейс програми доступний українською, чеською, англійською та російською.",
            mf_4_title: "Масові операції", mf_4_desc: "Генеруйте договори для 50 працівників одночасно одним натисканням.",
            mf_5_title: "Глобальний пошук", mf_5_desc: "Шукайте працівників, фірми чи документи з будь-якого екрана (Ctrl+F).",
            mf_6_title: "Журнал активності", mf_6_desc: "Програма записує кожну дію: хто, коли і що змінив або видалив.",
            mf_7_title: "Теми та кольори", mf_7_desc: "Світла та темна теми, вибір акцентного кольору під ваш настрій.",
            mf_8_title: "HR Новини", mf_8_desc: "Вбудована стрічка новин трудового законодавства з AI-перекладом.",
            mf_9_title: "Авто-оновлення", mf_9_desc: "Програма сама завантажує нові версії у фоновому режимі (Velopack).",

            pricing_tag: "Тарифи", pricing_title: "Прозоре ціноутворення", pricing_subtitle: "Виберіть план, який найкраще підходить для вашого бізнесу. Без прихованих платежів.",
            price_std_title: "Стандарт", price_year: " CZK / рік", price_std_desc: "Повний функціонал для управління агентством без використання штучного інтелекту.",
            price_f_emp_unlim: "Необмежено працівників", price_f_docs: "Генерація всіх документів", price_f_fin_inv: "Фінанси та Рахунки-фактури", price_f_updates: "Безкоштовні оновлення", price_f_no_ai: "Без AI-функцій",
            price_btn_start: "Почати 14 днів безкоштовно", price_recommended: "Рекомендовано", price_ult_title: "Ultimate AI", price_ult_desc: "Максимальна автоматизація завдяки інтеграції з Google Gemini.",
            price_f_all_std: "Все з тарифу \"Стандарт\"", price_f_ai_scan: "AI-сканування документів", price_f_ai_chat: "Вбудований AI-асистент (Чат)", price_f_ai_tags: "AI-автозаповнення шаблонів", price_f_ai_reports: "Автоматичні AI-звіти",
            integrations_title: "Працює з вашими улюбленими технологіями",
            included_title: "Усі тарифи включають:",
            inc_local: "Локальне зберігання даних (AES-256)",
            inc_updates: "Безкоштовні оновлення",
            inc_cloud: "Підтримка OneDrive / Google Drive",
            inc_lang: "Багатомовність (UK, CS, EN, RU)",
            inc_log: "Журнал активності",
            inc_backup: "Автоматичні бекапи",
            roi_tag: "Ефективність", roi_title: "Окупається за перший тиждень", roi_desc: "Подивіться, скільки часу ви втрачаєте на рутину щомісяця (з розрахунку на 50 працівників), і як Agency Contractor це змінює.",
            roi_without: "Без програми (Excel + Word)", roi_time1: "~35 годин", roi_tasks1: "Ручне введення даних, пошук помилок, контроль термінів",
            roi_with: "З Agency Contractor", roi_time2: "~2 години", roi_tasks2: "AI сканування, автогенерація в 1 клік, автоматичні сповіщення",
            roi_save_num: "94%", roi_save_text: "Зекономленого часу", roi_save_sub: "на кожного HR-менеджера",
            faq_title: "Поширені запитання", faq_q1: "Чи безпечні мої дані?", faq_a1: "Абсолютно. Ваші документи залишаються під вашим контролем. Усі секрети безпечно шифруються за допомогою Windows DPAPI, а доступ до командної бази захищений логіном та паролем PostgreSQL.",
            faq_q2: "Чи потрібен інтернет для роботи?", faq_a2: "Основний функціонал програми працює повністю офлайн. Інтернет потрібен лише для функцій AI (Google Gemini), інтеграції з ARES (реєстр Чехії) та перевірки ліцензії.",
            faq_q3: "Як працює 14-денний тестовий період?", faq_a3: "Ви завантажуєте програму і отримуєте повний доступ до всіх функцій (рівень \"Безліміт\") на 14 днів. Жодної прив'язки картки. Після завершення періоду ви зможете обрати зручний тариф.",
            faq_q4: "Чому Windows або антивірус блокує файл?", faq_a4: "Оскільки програма нова і розробник ще не має накопиченого \"рейтингу довіри\" від Microsoft (SmartScreen), система може попередити про невідомий файл. Це абсолютно безпечно. При завантаженні дозвольте збереження файлу, а при запуску натисніть \"Докладніше\" (More info) та \"Виконати в будь-якому випадку\" (Run anyway).",
            faq_q5: "Який саме файл потрібно скачувати?", faq_a5: "На сторінці GitHub Releases завжди обирайте найновішу версію (позначка Latest). Для встановлення програми вам потрібен файл із назвою <strong>AgencyContractor-win-Setup.exe</strong>.", faq_q6: "На яких комп'ютерах працює програма?", faq_a6: "Програма оптимізована для Windows 10 та Windows 11. Для комфортної роботи достатньо звичайного офісного комп'ютера (мінімум 4 ГБ оперативної пам'яті).", faq_q7: "Як працює технічна підтримка?", faq_a7: "Ми надаємо підтримку через Telegram. Також у програмі є вбудована база знань та покрокові інструкції.", social_proof: "«Один із наших клієнтів з базою у 200+ активних працівників скоротив час на щомісячний розрахунок зарплат, вирахування авансів та генерацію фактур з 4-х днів до 3-х годин.»" ,
            replace_tag: "Екосистема", replace_title: "Замінює хаос на систему", replace_subtitle: "Навіщо платити за 5 різних сервісів і плутатись у вікнах? Усе необхідне вже тут.", replace_all: "Все в одному місці",
            perfect_tag: "Для кого", perfect_title: "Ідеально підходить для вашого бізнесу", perfect_subtitle: "Програма адаптується під різні моделі роботи з персоналом",
            perfect_c1_title: "Кадрові агенції", perfect_c1_1: "Масовий найм та облік", perfect_c1_2: "Контроль термінів віз та дозволів", perfect_c1_3: "Розподіл по фірмах-клієнтах", perfect_c1_4: "Генерація фактур за послуги",
            perfect_c2_title: "Прямі роботодавці", perfect_c2_1: "Заводи, склади, виробництва", perfect_c2_2: "Облік годин та змін", perfect_c2_3: "Автоматизація договорів", perfect_c2_4: "Надійний локальний архів",
            perfect_c3_title: "HR та Бухгалтери", perfect_c3_1: "Зарплатні відомості без помилок", perfect_c3_2: "Швидкий розрахунок авансів", perfect_c3_3: "AI-помічник з кадрових питань", perfect_c3_4: "Експорт звітів в Excel/PDF",
            roi_emp_label: "Кількість працівників:", roi_bad_title: "Без програми (Витрати часу)", roi_hours: "год/міс", roi_bad_desc: "Ручне введення, пошук помилок, папери", roi_good_title: "З Agency Contractor", roi_good_desc: "Фіксована ціна 10 000 CZK/рік. Безліміт працівників, AI автоматизація, генерація в 1 клік.", roi_saved_text: "Економія:",
            roi_bd_docs: "📄 Документи:", roi_bd_salary: "💰 Зарплати:", roi_bd_invoices: "🧾 Фактури:", roi_bd_errors: "⚠️ Помилки:",
            marquee_1: "✨ Powered by Google Gemini AI", marquee_2: "🔒 Захист DPAPI та PostgreSQL", marquee_3: "⚡ Live Sync та OneDrive", marquee_4: "🏢 ARES Інтеграція", marquee_5: "⚡ Генерація в 1 клік",
            toast_1: "⚡ AI щойно згенерував 15 договорів", toast_2: "✅ Зарплати для 50 працівників розраховані за 1.2 сек", toast_3: "🔒 Резервна копія успішно створена", toast_4: "📄 Новий шаблон додано до бази", toast_5: "🔍 ARES оновив дані 3 компаній", toast_time: "щойно",
            sticky_cta_text: "Готові оптимізувати роботу?", sticky_cta_btn: "Почати безкоштовно",
            stat_1_title: "Менше ручної роботи", stat_2_title: "Днів безкоштовно", stat_3_title: "Мови інтерфейсу", stat_4_title: "Єдина база",
            dash_tag: "Огляд", dash_title: "Інтерактивний дашборд", dash_desc: "Центр управління з повною картиною стану вашого агентства в реальному часі", dash_f1: "<strong>Статистичні віджети</strong> — кількість працівників, проблем, шаблонів, компаній з трендами", dash_f2: "<strong>Прострочені документи</strong> — список з пріоритизацією: прострочені → критичні → попередження", dash_f3: "<strong>Зарплатна панель</strong> — підсумки по місяцях: gross, net, виплачено, аванси", dash_f4: "<strong>Статистика компаній</strong> — порівняння фірм за кількістю працівників та проблем", dash_f5: "<strong>AI-звіт</strong> — автоматична аналітична записка через Google Gemini", dash_f6: "<strong>Гнучке компонування</strong> — drag & drop віджетів, зміна пропорцій, збереження layout",
            emp_tag: "Ядро системи", emp_title: "Управління працівниками", emp_desc: "Повний цикл роботи з персоналом — від створення до архівації", emp_f1: "<strong>Покроковий wizard</strong> — 9 кроків: документи, фото, паспорт, страхівка, віза, дозвіл, адреси. Адаптується під тип працівника", emp_f2: "<strong>AI-сканування</strong> — автоматичне розпізнавання даних з документів через Gemini на кожному кроці", emp_f3: "<strong>Гнучкі звіти</strong> — налаштовуйте відображення колонок під себе: як у таблицях обліку, так і при експорті", emp_f4: "<strong>Деталізоване досьє</strong> — номер та орган видачі візи, громадянство, дати документів, актуальна фірма, агенція та статус", emp_f5: "<strong>Масові операції</strong> — мультивибір, пакетна генерація документів, експорт в Excel", emp_f6: "<strong>Профіль</strong> — 4 вкладки (документи, профіль, історія, зарплата), індикатор заповненості, попередження про терміни",
            doc_tag: "Контроль", doc_title: "Документи та моніторинг", doc_desc: "Захист від штрафів міграційної поліції. Один прострочений дозвіл може коштувати вашому бізнесу десятків тисяч крон. Програма заздалегідь підсвітить критичні терміни.", doc_f1: "<strong>Паспорт, віза, страхівка, дозвіл</strong> — заміна, перегляд, подовження з діалогами", doc_f2: "<strong>Кастомні документи</strong> — власні підписані документи з датою підпису та терміном дії", doc_f3: "<strong>3 рівні серйозності</strong> — Warning, Critical, Expired з кольоровими індикаторами", doc_f4: "<strong>Агрегація по всіх фірмах</strong> — єдиний екран для всіх проблем усіх компаній", doc_f5: "<strong>Snooze</strong> — ігнорування проблеми на 7/14/30/60/90 днів з відновленням", doc_f6: "<strong>PDF-звіт</strong> — експорт проблем з кольоровим маркуванням і статистикою",
            tpl_tag: "Автоматизація", tpl_title: "Шаблони документів", tpl_desc: "Створюйте документи за секунди — редактори для кожного формату", tpl_f1: "<strong>DOCX-редактор</strong> — вбудований RTF-редактор з форматуванням, каталогом тегів, налаштуванням сторінки", tpl_f2: "<strong>XLSX-редактор</strong> — табличний перегляд з об'єднаними комірками та вставкою тегів", tpl_f3: "<strong>PDF-редактор</strong> — візуальне розміщення тегів drag & drop з налаштуванням шрифту та позиції", tpl_f4: "<strong>AI-вставка тегів</strong> — Gemini автоматично розміщує змінні в правильні місця", tpl_f5: "<strong>Стартові шаблони</strong> — каталог готових шаблонів для швидкого старту", tpl_f6: "<strong>Копіювання між фірмами</strong> — перенесення шаблону в іншу компанію одним кліком",
            fin_tag: "Облік", fin_title: "Фінанси та зарплати", fin_desc: "Помісячний облік зарплат, авансів та витрат з повним контролем", fin_f1: "<strong>Зарплатна таблиця</strong> — gross, net, години, аванси, стан оплати по кожному працівнику", fin_f2: "<strong>Робота по різних фірмах</strong> — програма легко обробляє сценарії, коли працівник працював на кількох фірмах в одному місяці", fin_f3: "<strong>Аванси</strong> — додавання з назвою, сумою, нотаткою; авансові PDF-таблиці по фірмах", fin_f4: "<strong>Витрати фірми</strong> — окремий облік витрат компанії", fin_f5: "<strong>Командна стабільність</strong> — надійний захист від дублювання історії виплат та мінімізація конфліктів при одночасній роботі", fin_f6: "<strong>Підписні листи</strong> — PDF з підписами по місяцях, landscape A4",
            rep_tag: "Аналітика", rep_title: "Звіти та аналітика", rep_desc: "Глибока аналітика з графіками, фільтрами та експортом", rep_f1: "<strong>Діапазон дат</strong> — гнучкий вибір періоду з автоматичним збереженням", rep_f2: "<strong>Фільтри по фірмах та агенціях</strong> — включення/виключення з toggle all", rep_f3: "<strong>Метрики</strong> — активні, нові, завершили, архівовані, відновлені за період", rep_f4: "<strong>Графік архіву</strong> — помісячна динаміка архівацій та відновлень (OxyPlot)", rep_f5: "<strong>Два режими</strong> — зведений звіт або детальний список працівників", rep_f6: "<strong>Експорт</strong> — вибір аркушів (фірми, агенції, працівники, архів) у XLSX або PDF",
            inv_tag: "Бухгалтерія", inv_title: "Рахунки-фактури", inv_desc: "Повний модуль виставлення рахунків з QR-платежами та PDF-шаблонами", inv_f1: "<strong>5 типів документів</strong> — рахунок, цінова пропозиція, замовлення, прибутковий та видатковий касові ордери", inv_f2: "<strong>ARES-інтеграція</strong> — автозаповнення реквізитів по IČO з чеського реєстру", inv_f3: "<strong>QR-платежі</strong> — формати SPAYD / EPC з живим превю коду", inv_f4: "<strong>6 PDF-шаблонів + 4 теми</strong> — стилізовані рахунки з логотипом і печаткою", inv_f5: "<strong>4 валюти</strong> — CZK, EUR, USD, PLN з знижками та частковою оплатою", inv_f6: "<strong>Каталоги</strong> — збережені постачальники, замовники, товари для швидкого заповнення",
            ai_tag: "Штучний інтелект", ai_title: "AI-інтеграція Gemini", ai_desc: "Google Gemini вбудований у кожен аспект програми", ai_f1: "<strong>AI-чат</strong> — спеціалізований асистент для кадрових агенцій: трудове право, документи, дозволи", ai_f2: "<strong>Сканування документів</strong> — автоматичне розпізнавання даних з паспортів, віз, страхівок", ai_f3: "<strong>AI-пошук</strong> — знаходження працівників природною мовою", ai_f4: "<strong>AI-вставка тегів</strong> — автоматичне розміщення змінних у шаблонах", ai_f5: "<strong>AI-звіт дашборда</strong> — аналітична записка по всіх метриках", ai_f6: "<strong>Новини з AI</strong> — переклад, аналіз, запитання до статей стрічки новин",
            arch_tag: "Захист даних", arch_title: "Архів та м'яке видалення", arch_desc: "Жодні дані не втрачаються — повний контроль над життєвим циклом працівника", arch_f1: "<strong>Архів</strong> — окремий процес завершення співпраці з датою; пошук, фільтри, відновлення", arch_f2: "<strong>Нещодавно видалені</strong> — м'яке видалення з можливістю відновлення, архівації або остаточного видалення", arch_f3: "<strong>Відміна дій</strong> — кнопка Undo для нещодавніх архівацій з підтвердженням паролю", arch_f4: "<strong>Перегляд профілю</strong> — read-only доступ до профілю видалених та архівних працівників", arch_f5: "<strong>Історія дій</strong> — повний журнал всіх операцій з фільтрами, категоріями, експортом в Excel", arch_f6: "<strong>Розумний Backup</strong> — щоденне копіювання в Backup\YYYY-MM-DD (7 днів). У командному режимі автоматичний експорт PostgreSQL → SQLite",
            comp_tag: "Організація", comp_title: "Компанії та кандидати", comp_desc: "Управління фірмами-роботодавцями та базою кандидатів", comp_f1: "<strong>Фірми-роботодавці</strong> — назва, IČO, юридична адреса, до 4 робочих адрес, позиції із зарплатами", comp_f2: "<strong>Агенція</strong> — прив'язка агенції (назва, IČO, адреса) до кожної фірми", comp_f3: "<strong>Графік роботи</strong> — тижневі/денні години, кількість змін", comp_f4: "<strong>Видимість</strong> — приховування фірм без видалення; перевірка на активних працівників", comp_f5: "<strong>Кандидати</strong> — окрема база з профілями, фото, документами; пошук по позиції", comp_f6: "<strong>Глобальний пошук</strong> — знаходження працівників, шаблонів, кандидатів, архіву з однієї стрічки",
            start_tag: "Швидкий старт", start_title: "Почати можна за кілька хвилин", start_subtitle: "Встановіть програму, створіть профіль і поступово перенесіть роботу з Excel, Word та папок у єдину систему.",
            start_1_title: "Завантажте Setup.exe", start_1_desc: "Остання версія завжди доступна на GitHub Releases з автооновленням.",
            start_2_title: "Додайте фірму", start_2_desc: "Вкажіть реквізити, адреси, позиції, графік роботи та агенцію.",
            start_3_title: "Створіть працівника", start_3_desc: "Wizard, AI-сканування документів, фото, адреси, історія та контроль термінів.",
            start_4_title: "Підключіть AI або Telegram", start_4_desc: "Отримуйте відповіді по базі, зарплатах, документах і звітах прямо з програми або телефону.",
            cta_title: "Готові оптимізувати<br>роботу вашого агентства?", cta_desc: "Agency Contractor — все необхідне для кадрового агентства в одній програмі", cta_btn: "Почати безкоштовний період", cta_f1: "4 мови", cta_f2: "Шифрування",
            mock_dash_title: "Дашборд", mock_dash_crit: "Критичні документи", mock_emp_title: "Працівники", mock_emp_scan: "AI Сканування документу",
            mock_doc_title: "Документи", mock_doc_th1: "Документ", mock_doc_th2: "Працівник", mock_doc_th3: "Статус", mock_doc_th4: "Дія", mock_doc_pass: "Паспорт", mock_doc_visa: "Віза", mock_doc_ins: "Страхівка", mock_doc_st_exp: "Прострочено", mock_doc_st_warn: "Увага", mock_doc_st_ok: "Ок", mock_doc_pop_title: "Увага: Документ прострочено!", mock_doc_pop_desc: "Паспорт Івана Петренка закінчився 2 дні тому.", mock_doc_pop_btn1: "Ігнорувати", mock_doc_pop_btn2: "Подовжити",
            mock_tpl_title: "Шаблони", mock_fin_title: "Фінанси", mock_fin_th1: "Працівник", mock_fin_th2: "Години", mock_fin_th3: "Аванси", mock_fin_th4: "До виплати", mock_fin_calc_title: "Розрахунок: Іван Петренко", mock_fin_calc_h: "Години:", mock_fin_calc_r: "Ставка:", mock_fin_calc_t: "Разом:",
            mock_rep_title: "Звіти", mock_inv_title: "Рахунки", mock_inv_btn: "Згенерувати PDF", mock_inv_stamp: "PAID", mock_inv_total: "Разом: 25 000 CZK",
            mock_ai_title: "AI Асистент", mock_ai_msg1: "Проаналізуй договір працівника", mock_ai_msg2: "Договір відповідає стандартам. Зверніть увагу на пункт 4.2 щодо випробувального терміну.", mock_ai_msg3: "Які документи потрібні для візи?",
            mock_arch_th2: "Дата архівації", mock_arch_th3: "Причина", mock_arch_r1: "Закінчення контракту", mock_arch_r2: "За власним бажанням", mock_arch_r3: "Перехід на іншу фірму",
            mock_comp_title: "Компанії", mock_sec_title: "Безпека", mock_sec_pass: "Паспорт: EX12345",
            mock_tg_title: "Agency Contractor Bot", mock_tg_date: "Сьогодні", mock_tg_voice: "\"Знайди Івана Петренка\"", mock_tg_msg1: "👤 <b>Іван Петренко</b><br>Статус: Активний ✅<br>Фірма: Skoda Auto<br>Телефон: +420 123 456 789<br>Документи: Паспорт до 12.2026", mock_tg_msg2: "А яка в нього зарплата за травень?", mock_tg_msg3: "💰 <b>Зарплата: Травень 2026</b><br>Години: 180<br>Ставка: 150 CZK<br>Аванси: -5 000 CZK<br><b>До виплати: 22 000 CZK</b>",
            name_ivan: "Іван Петренко", name_maria: "Марія Коваль", name_oleg: "Олег Сидор"
        },
        cs: {
            nav_features: "Funkce", nav_employees: "Zaměstnanci", nav_templates: "Šablony", nav_finance: "Finance", nav_invoices: "Faktury", nav_ai: "AI",
            hero_title: "Zaměstnanci, dokumenty, mzdy a AI<br><span class='gradient-text'>v jednom systému</span>",
            hero_subtitle: "Agency Contractor nahrazuje Excel, Word, složky a ruční kontrolu termínů jedním programem pro agentury, zaměstnavatele, HR a účetní.",
            hero_btn_download: "Stáhnout zdarma (14 dní)", hero_btn_features: "Zobrazit funkce",
            hero_proof_1: "Lokální nebo Síťová DB", hero_proof_2: "Telegram bot pro manažery", hero_proof_3: "14 dní zdarma",
            stat_modules: "Modulů", stat_languages: "Jazyky", stat_templates: "Šablon",
            outcome_tag: "Výsledek", outcome_title: "Místo chaosu kontrola nad každým procesem", outcome_subtitle: "Program data nejen ukládá. Ukazuje, co je potřeba udělat, kde jsou rizika a která čísla jsou dnes důležitá.",
            outcome_1_title: "Méně ruční práce", outcome_1_desc: "AI skenování dokumentů, šablony, automatické vyplňování a hromadné generování odstraňují opakované úkony.",
            outcome_2_title: "Kontrola dokumentů", outcome_2_desc: "Pas, vízum, pojištění, pracovní povolení i vlastní dokumenty jsou hlídány automaticky.",
            outcome_3_title: "Finance bez zmatku", outcome_3_desc: "Mzdy, zálohy, dluhy, firemní výdaje, PDF/Excel exporty a měsíční souhrny na jednom místě.",
            outcome_4_title: "Řízení z telefonu", outcome_4_desc: "Telegram bot odpovídá na otázky o zaměstnancích, mzdách, dokumentech a analytice hlasem nebo textem.",
            chaos_1: "Dokumenty", chaos_2: "Zaměstnanci", chaos_3: "Mzdy", chaos_4: "AI + Telegram",
            feat_tag: "Možnosti", feat_title: "Vše pro správu agentury", feat_subtitle: "18 modulů pokrývajících každý aspekt práce personální agentury",
            feat_dash_title: "Nástěnka", feat_dash_desc: "Interaktivní panel s widgety, statistikami, AI reporty a flexibilním rozložením",
            feat_emp_title: "Zaměstnanci", feat_emp_desc: "Plný cyklus: od vytvoření přes průvodce až po archivaci, s AI skenováním dokumentů",
            feat_tpl_title: "Šablony", feat_tpl_desc: "Editory DOCX, XLSX, PDF s katalogem značek a AI vkládáním proměnných",
            feat_fin_title: "Finance", feat_fin_desc: "Platové tabulky, zálohy, výdaje, export — měsíční účtování",
            feat_rep_title: "Reporty", feat_rep_desc: "Analytika pro firmy, agentury, zaměstnance s grafy a exportem do PDF/Excel",
            feat_inv_title: "Faktury", feat_inv_desc: "Faktury, cenové nabídky, pokladní doklady s QR platbami a ARES",
            feat_ai_title: "AI asistent", feat_ai_desc: "Vestavěný chat Gemini se specializací na pracovní právo a dokumenty",
            feat_sec_title: "Bezpečnost", feat_sec_desc: "Šifrování AES/DPAPI, hesla PBKDF2, potvrzení pro nebezpečné akce",
            feat_tg_title: "Telegram Bot", feat_tg_desc: "Správa agentury z telefonu: vyhledávání, platy, analytika a hlasový AI asistent",
            tg_tag: "Mobilita", tg_title: "Telegram Bot pro manažery", tg_desc: "Okamžitý přístup k celé databázi agentury přímo z vašeho telefonu — bez nutnosti otevírat počítač.",
            tg_f1: "<strong>Hlasové AI vyhledávání</strong> — stačí poslat hlasovou zprávu, bot rozpozná řeč a najde zaměstnance, firmu nebo report.",
            tg_f2: "<strong>Chytrý kontext</strong> — bot si pamatuje konverzaci. Zeptejte se \"Najdi Olega\" a pak jen napište \"A jaké má zálohy?\".",
            tg_f3: "<strong>Finance v kapse</strong> — platy za jakýkoli měsíc, shrnutí po firmách, porovnání měsíců (delta) a top výplaty.",
            tg_f4: "<strong>Kontrola dokumentů</strong> — rychlý dotaz na seznam dokumentů, kterým končí platnost za 7 nebo 30 dní.",
            tg_f5: "<strong>HR Analytika</strong> — statistika přijatých/propuštěných za měsíc a čistá bilance pohybu zaměstnanců.",
            tg_f6: "<strong>HR Novinky</strong> — získejte aktuální informace o migraci a legislativě přímo do chatu.",
            sec_tag: "Architektura", sec_title: "Flexibilní architektura a Týmová práce", sec_desc: "Program se přizpůsobí rozsahu vašeho podnikání: od samostatné práce na jednom počítači až po plnohodnotný síťový systém pro celý tým.",
            sec_f1_title: "Pro jeden PC (SQLite)", sec_f1_desc: "— pracujte maximálně rychle s lokální databází. Všechna data jsou uložena výhradně na vašem počítači.",
            sec_f2_title: "Synchronizace (OneDrive)", sec_f2_desc: "— umístěte soubor databáze a dokumenty na cloudové úložiště, abyste měli přístup k pracovnímu prostoru z různých počítačů střídavě.",
            sec_f3_title: "Týmová práce (PostgreSQL)", sec_f3_desc: "— spojte 3–5+ uživatelů. Nasaďte databázi na hlavní PC nebo server a připojte další zaměstnance přes LAN nebo Tailscale.",
            sec_f4_title: "Live Sync (Okamžitá synchronizace)", sec_f4_desc: "— při práci přes PostgreSQL se všechny změny mezi PC přenášejí okamžitě. Ostatní uživatelé vidí aktualizace bez zpoždění.",
            
            more_tag: "Další", more_title: "Více než jen databáze", more_subtitle: "Dalších 9 funkcí, které zrychlí a zpříjemní vaši každodenní práci",
            mf_1_title: "Editor obrázků", mf_1_desc: "Ořezávání, otáčení a komprese fotografií a skenů přímo v programu.",
            mf_2_title: "Automatické zálohy", mf_2_desc: "Vytváření záloh před důležitými změnami. Obnova jedním kliknutím.",
            mf_3_title: "Vícejazyčnost", mf_3_desc: "Rozhraní programu je dostupné v ukrajinštině, češtině, angličtině a ruštině.",
            mf_4_title: "Hromadné operace", mf_4_desc: "Generujte smlouvy pro 50 zaměstnanců najednou jedním kliknutím.",
            mf_5_title: "Globální vyhledávání", mf_5_desc: "Hledejte zaměstnance, firmy nebo dokumenty z jakékoli obrazovky (Ctrl+F).",
            mf_6_title: "Deník aktivity", mf_6_desc: "Program zaznamenává každou akci: kdo, kdy a co změnil nebo smazal.",
            mf_7_title: "Témata a barvy", mf_7_desc: "Světlé a tmavé téma, výběr akcentní barvy podle vaší nálady.",
            mf_8_title: "HR Novinky", mf_8_desc: "Vestavěný kanál novinek z pracovního práva s AI překladem.",
            mf_9_title: "Automatické aktualizace", mf_9_desc: "Program sám stahuje nové verze na pozadí (Velopack).",

            pricing_tag: "Tarify", pricing_title: "Transparentní ceny", pricing_subtitle: "Vyberte si plán, který nejlépe vyhovuje vašemu podnikání. Bez skrytých poplatků.",
            price_std_title: "Standard", price_year: " CZK / rok", price_std_desc: "Plná funkčnost pro správu agentury bez využití umělé inteligence.",
            price_f_emp_unlim: "Neomezeně zaměstnanců", price_f_docs: "Generování všech dokumentů", price_f_fin_inv: "Finance a faktury", price_f_updates: "Aktualizace zdarma", price_f_no_ai: "Bez AI funkcí",
            price_btn_start: "Začít 14 dní zdarma", price_recommended: "Doporučeno", price_ult_title: "Ultimate AI", price_ult_desc: "Maximální automatizace díky integraci s Google Gemini.",
            price_f_all_std: "Vše z tarifu \"Standard\"", price_f_ai_scan: "AI skenování dokumentů", price_f_ai_chat: "Vestavěný AI asistent (Chat)", price_f_ai_tags: "AI automatické vyplňování šablon", price_f_ai_reports: "Automatické AI reporty",
            integrations_title: "Pracuje s vašimi oblíbenými technologiemi",
            included_title: "Všechny tarify zahrnují:",
            inc_local: "Lokální ukládání dat (AES-256)",
            inc_updates: "Bezplatné aktualizace",
            inc_cloud: "Podpora OneDrive / Google Drive",
            inc_lang: "Vícejazyčnost (UK, CS, EN)",
            inc_log: "Deník aktivit",
            inc_backup: "Automatické zálohy",
            roi_tag: "Efektivita", roi_title: "Vrátí se za první týden", roi_desc: "Podívejte se, kolik času ztrácíte rutinou každý měsíc (počítáno na 50 zaměstnanců) a jak to Agency Contractor mění.",
            roi_without: "Bez programu (Excel + Word)", roi_time1: "~35 hodin", roi_tasks1: "Ruční zadávání dat, hledání chyb, kontrola termínů",
            roi_with: "S Agency Contractor", roi_time2: "~2 hodiny", roi_tasks2: "AI skenování, automatické generování, automatická upozornění",
            roi_save_num: "94%", roi_save_text: "Ušetřeného času", roi_save_sub: "na každého HR manažera",
            faq_title: "Často kladené otázky", faq_q1: "Jsou moje data v bezpečí?", faq_a1: "Absolutně. Všechna data jsou uložena lokálně ve vašem počítači a šifrována pomocí algoritmu AES-256 a Windows DPAPI. K vaší databázi nemáme přístup.",
            faq_q2: "Potřebuji k práci internet?", faq_a2: "Základní funkce programu fungují zcela offline. Internet je vyžadován pouze pro funkce AI (Google Gemini), integraci s ARES (český registr) a ověření licence.",
            faq_q3: "Jak funguje 14denní zkušební doba?", faq_a3: "Stáhnete si program a získáte plný přístup ke všem funkcím (úroveň \"Neomezený\") na 14 dní. Žádné vázání karty. Po uplynutí doby si můžete vybrat vhodný tarif.",
            faq_q4: "Proč Windows nebo antivirus blokuje soubor?", faq_a4: "Jelikož je program nový a vývojář ještě nemá \"hodnocení důvěryhodnosti\" od Microsoftu (SmartScreen), systém může varovat před neznámým souborem. Je to naprosto bezpečné. Při stahování povolte uložení souboru a při spuštění klikněte na \"Další informace\" (More info) a \"Přesto spustit\" (Run anyway).",
            faq_q5: "Který soubor si mám stáhnout?", faq_a5: "Na stránce GitHub Releases vždy vybírejte nejnovější verzi (označení Latest). Pro instalaci programu potřebujete soubor s názvem <strong>AgencyContractor-win-Setup.exe</strong>.", faq_q6: "Na jakých počítačích program funguje?", faq_a6: "Program je optimalizován pro Windows 10 a Windows 11. Pro pohodlnou práci stačí běžný kancelářský počítač (minimálně 4 GB RAM).", faq_q7: "Jak funguje technická podpora?", faq_a7: "Podporu poskytujeme přes Telegram. V programu je také vestavěná znalostní báze a návody krok za krokem.", social_proof: "«Jeden z našich klientů s databází 200+ aktivních zaměstnanců zkrátil čas na měsíční výpočet mezd, záloh a faktur ze 4 dnů na 3 hodiny.»" ,
            replace_tag: "Ekosystém", replace_title: "Mění chaos v systém", replace_subtitle: "Proč platit za 5 různých služeb a zmatkovat v oknech? Vše potřebné je už tady.", replace_all: "Vše na jednom místě",
            perfect_tag: "Pro koho", perfect_title: "Ideální pro vaše podnikání", perfect_subtitle: "Program se přizpůsobí různým modelům práce s personálem",
            perfect_c1_title: "Personální agentury", perfect_c1_1: "Hromadný nábor a evidence", perfect_c1_2: "Kontrola termínů víz a povolení", perfect_c1_3: "Rozdělení podle klientských firem", perfect_c1_4: "Generování faktur za služby",
            perfect_c2_title: "Přímí zaměstnavatelé", perfect_c2_1: "Továrny, sklady, výroba", perfect_c2_2: "Evidence hodin a směn", perfect_c2_3: "Automatizace smluv", perfect_c2_4: "Spolehlivý lokální archiv",
            perfect_c3_title: "HR a Účetní", perfect_c3_1: "Mzdové výkazy bez chyb", perfect_c3_2: "Rychlý výpočet záloh", perfect_c3_3: "AI asistent pro HR otázky", perfect_c3_4: "Export reportů do Excel/PDF",
            roi_emp_label: "Počet zaměstnanců:", roi_bad_title: "Bez programu (Ztráta času)", roi_hours: "hod/měs", roi_bad_desc: "Ruční zadávání, hledání chyb, papíry", roi_good_title: "S Agency Contractor", roi_good_desc: "Fixní cena 10 000 CZK/rok. Neomezeně zaměstnanců, AI automatizace, generování 1 kliknutím.", roi_saved_text: "Úspora:",
            roi_bd_docs: "📄 Dokumenty:", roi_bd_salary: "💰 Platy:", roi_bd_invoices: "🧾 Faktury:", roi_bd_errors: "⚠️ Chyby:",
            marquee_1: "✨ Powered by Google Gemini AI", marquee_2: "🔒 AES-256 Šifrování", marquee_3: "☁️ OneDrive Synchronizace", marquee_4: "🏢 ARES Integrace", marquee_5: "⚡ Generování 1 kliknutím",
            toast_1: "⚡ AI právě vygenerovala 15 smluv", toast_2: "✅ Platy pro 50 zaměstnanců vypočítány za 1.2 s", toast_3: "🔒 Záloha byla úspěšně vytvořena", toast_4: "📄 Nová šablona přidána do databáze", toast_5: "🔍 ARES aktualizoval data 3 firem", toast_time: "právě teď",
            sticky_cta_text: "Připraveni optimalizovat práci?", sticky_cta_btn: "Začít zdarma",
            stat_1_title: "Méně ruční práce", stat_2_title: "Dní zdarma", stat_3_title: "Jazyky rozhraní", stat_4_title: "Jedna databáze",
            dash_tag: "Přehled", dash_title: "Interaktivní nástěnka", dash_desc: "Řídicí centrum s úplným přehledem o stavu vaší agentury v reálném čase", dash_f1: "<strong>Statistické widgety</strong> — počet zaměstnanců, problémů, šablon, firem s trendy", dash_f2: "<strong>Propadlé dokumenty</strong> — seznam s prioritami: propadlé → kritické → varování", dash_f3: "<strong>Platový panel</strong> — shrnutí po měsících: hrubá, čistá mzda, vyplaceno, zálohy", dash_f4: "<strong>Statistika firem</strong> — porovnání firem podle počtu zaměstnanců a problémů", dash_f5: "<strong>AI report</strong> — automatická analytická zpráva přes Google Gemini", dash_f6: "<strong>Flexibilní rozložení</strong> — drag & drop widgetů, změna proporcí, uložení layoutu",
            emp_tag: "Jádro systému", emp_title: "Správa zaměstnanců", emp_desc: "Plný cyklus práce s personálem — od vytvoření po archivaci", emp_f1: "<strong>Průvodce krok za krokem</strong> — 9 kroků: dokumenty, foto, pas, pojištění, vízum, povolení, adresy. Přizpůsobí se typu zaměstnance", emp_f2: "<strong>AI skenování</strong> — automatické rozpoznávání dat z dokumentů přes Gemini na každém kroku", emp_f3: "<strong>Flexibilní reporty</strong> — přizpůsobte si zobrazení sloupců podle sebe: jak v tabulkách, tak při exportu", emp_f4: "<strong>Hledání a filtry</strong> — podle jména, čísel dokladů, stavu, problémů, nových v tomto měsíci", emp_f5: "<strong>Hromadné operace</strong> — vícenásobný výběr, dávkové generování dokumentů, export do Excelu", emp_f6: "<strong>Profil</strong> — 4 záložky (dokumenty, profil, historie, plat), indikátor vyplnění, varování o termínech",
            doc_tag: "Kontrola", doc_title: "Dokumenty a monitoring", doc_desc: "Ochrana před pokutami cizinecké policie. Jedno propadlé povolení může stát váš podnik desítky tisíc korun. Program včas zvýrazní kritické termíny.", doc_f1: "<strong>Pas, vízum, pojištění, povolení</strong> — výměna, prohlížení, prodloužení s dialogy", doc_f2: "<strong>Vlastní dokumenty</strong> — vlastní podepsané dokumenty s datem podpisu a platností", doc_f3: "<strong>3 úrovně závažnosti</strong> — Varování, Kritické, Propadlé s barevnými indikátory", doc_f4: "<strong>Agregace za všechny firmy</strong> — jediná obrazovka pro všechny problémy všech společností", doc_f5: "<strong>Odložit (Snooze)</strong> — ignorování problému na 7/14/30/60/90 dní s obnovením", doc_f6: "<strong>PDF report</strong> — export problémů s barevným značením a statistikou",
            tpl_tag: "Automatizace", tpl_title: "Šablony dokumentů", tpl_desc: "Vytvářejte dokumenty za vteřiny — editory pro každý formát", tpl_f1: "<strong>DOCX editor</strong> — vestavěný RTF editor s formátováním, katalogem tagů, nastavením stránky", tpl_f2: "<strong>XLSX editor</strong> — tabulkové zobrazení se sloučenými buňkami a vkládáním tagů", tpl_f3: "<strong>PDF editor</strong> — vizuální umístění tagů drag & drop s nastavením písma a pozice", tpl_f4: "<strong>AI vkládání tagů</strong> — Gemini automaticky umístí proměnné na správná místa", tpl_f5: "<strong>Výchozí šablony</strong> — katalog hotových šablon pro rychlý start", tpl_f6: "<strong>Kopírování mezi firmami</strong> — přenos šablony do jiné společnosti jedním kliknutím",
            fin_tag: "Účtování", fin_title: "Finance a platy", fin_desc: "Měsíční evidence platů, záloh a výdajů s plnou kontrolou", fin_f1: "<strong>Platová tabulka</strong> — hrubá, čistá mzda, hodiny, zálohy, stav platby pro každého zaměstnance", fin_f2: "<strong>Filtr firem</strong> — zobrazení platů zvlášť pro každou společnost", fin_f3: "<strong>Zálohy</strong> — přidávání s názvem, částkou, poznámkou; zálohové PDF tabulky za firmy", fin_f4: "<strong>Výdaje firmy</strong> — samostatná evidence výdajů společnosti", fin_f5: "<strong>Týmová stabilita</strong> — spolehlivá ochrana proti duplicitní historii plateb a minimalizace konfliktů při současné práci", fin_f6: "<strong>Podpisové archy</strong> — PDF s podpisy po měsících, na šířku A4",
            rep_tag: "Analytika", rep_title: "Reporty a analytika", rep_desc: "Hluboká analytika s grafy, filtry a exportem", rep_f1: "<strong>Časové rozpětí</strong> — flexibilní výběr období s automatickým uložením", rep_f2: "<strong>Filtry firem a agentur</strong> — zahrnutí/vyloučení s možností toggle all", rep_f3: "<strong>Metriky</strong> — aktivní, noví, ukončení, archivovaní, obnovení za období", rep_f4: "<strong>Graf archivu</strong> — měsíční dynamika archivací a obnovení (OxyPlot)", rep_f5: "<strong>Dva režimy</strong> — souhrnný report nebo detailní seznam zaměstnanců", rep_f6: "<strong>Export</strong> — výběr listů (firmy, agentury, zaměstnanci, archiv) do XLSX nebo PDF",
            inv_tag: "Účetnictví", inv_title: "Faktury", inv_desc: "Kompletní modul vystavování faktur s QR platbami a PDF šablonami", inv_f1: "<strong>5 typů dokumentů</strong> — faktura, cenová nabídka, objednávka, příjmový a výdajový pokladní doklad", inv_f2: "<strong>Integrace ARES</strong> — automatické vyplnění údajů podle IČO z českého registru", inv_f3: "<strong>QR platby</strong> — formáty SPAYD / EPC s živým náhledem kódu", inv_f4: "<strong>6 PDF šablon + 4 témata</strong> — stylované faktury s logem a razítkem", inv_f5: "<strong>4 měny</strong> — CZK, EUR, USD, PLN se slevami a částečnou úhradou", inv_f6: "<strong>Katalogy</strong> — uložené dodavatelé, odběratelé, položky pro rychlé vyplnění",
            ai_tag: "Umělá inteligence", ai_title: "AI integrace Gemini", ai_desc: "Google Gemini je zabudován do každého aspektu programu", ai_f1: "<strong>AI chat</strong> — specializovaný asistent pro personální agentury: pracovní právo, dokumenty, povolení", ai_f2: "<strong>Skenování dokumentů</strong> — automatické rozpoznávání dat z pasů, víz, pojištění", ai_f3: "<strong>AI vyhledávání</strong> — hledání zaměstnanců přirozeným jazykem", ai_f4: "<strong>AI vkládání tagů</strong> — automatické umístění proměnných do šablon", ai_f5: "<strong>AI report na nástěnce</strong> — analytická zpráva podle všech metrik", ai_f6: "<strong>Novinky s AI</strong> — překlad, analýza, dotazy k článkům v novinkách",
            arch_tag: "Ochrana dat", arch_title: "Archiv a měkké smazání", arch_desc: "Žádná data se neztratí — plná kontrola nad životním cyklem zaměstnance", arch_f1: "<strong>Archiv</strong> — samostatný proces ukončení spolupráce s datem; hledání, filtry, obnova", arch_f2: "<strong>Nedávno smazané</strong> — měkké smazání s možností obnovení, archivace nebo trvalého smazání", arch_f3: "<strong>Zrušení akce</strong> — tlačítko Undo pro nedávné archivace s potvrzením heslem", arch_f4: "<strong>Prohlížení profilu</strong> — read-only přístup k profilu smazaných a archivovaných zaměstnanců", arch_f5: "<strong>Historie akcí</strong> — úplný žurnál všech operací s filtry, kategoriemi, exportem do Excelu", arch_f6: "<strong>Chytrý Backup</strong> — každodenní kopírování do Backup\YYYY-MM-DD (7 dní). V týmovém režimu automatický export PostgreSQL → SQLite",
            comp_tag: "Organizace", comp_title: "Firmy a kandidáti", comp_desc: "Správa klientských firem a databáze kandidátů", comp_f1: "<strong>Klientské firmy</strong> — název, IČO, sídlo, až 4 pracovní adresy, pozice s platy", comp_f2: "<strong>Agentura</strong> — přiřazení agentury (název, IČO, adresa) ke každé firmě", comp_f3: "<strong>Pracovní doba</strong> — týdenní/denní hodiny, počet směn", comp_f4: "<strong>Viditelnost</strong> — skrytí firem bez smazání; kontrola aktivních zaměstnanců", comp_f5: "<strong>Kandidáti</strong> — samostatná databáze s profily, foto, dokumenty; hledání podle pozice", comp_f6: "<strong>Globální vyhledávání</strong> — hledání zaměstnanců, šablon, kandidátů, archivu z jednoho místa",
            start_tag: "Rychlý start", start_title: "Začít můžete během několika minut", start_subtitle: "Nainstalujte program, vytvořte profil a postupně přesuňte práci z Excelu, Wordu a složek do jednoho systému.",
            start_1_title: "Stáhněte Setup.exe", start_1_desc: "Nejnovější verze je vždy dostupná na GitHub Releases s automatickými aktualizacemi.",
            start_2_title: "Přidejte firmu", start_2_desc: "Zadejte údaje, adresy, pozice, pracovní dobu a agenturu.",
            start_3_title: "Vytvořte zaměstnance", start_3_desc: "Průvodce, AI skenování dokumentů, foto, adresy, historie a kontrola termínů.",
            start_4_title: "Připojte AI nebo Telegram", start_4_desc: "Získávejte odpovědi o databázi, mzdách, dokumentech a reportech přímo v programu nebo telefonu.",
            cta_title: "Jste připraveni optimalizovat<br>práci vaší agentury?", cta_desc: "Agency Contractor — vše potřebné pro personální agenturu v jednom programu", cta_btn: "Začít zkušební období", cta_f1: "4 jazyky", cta_f2: "Šifrování",
            mock_dash_title: "Nástěnka", mock_dash_crit: "Kritické dokumenty", mock_emp_title: "Zaměstnanci", mock_emp_scan: "AI Skenování dokumentu",
            mock_doc_title: "Dokumenty", mock_doc_th1: "Dokument", mock_doc_th2: "Zaměstnanec", mock_doc_th3: "Stav", mock_doc_th4: "Akce", mock_doc_pass: "Pas", mock_doc_visa: "Vízum", mock_doc_ins: "Pojištění", mock_doc_st_exp: "Propadlé", mock_doc_st_warn: "Varování", mock_doc_st_ok: "Ok", mock_doc_pop_title: "Varování: Dokument propadl!", mock_doc_pop_desc: "Pas Ivana Petrenka vypršel před 2 dny.", mock_doc_pop_btn1: "Ignorovat", mock_doc_pop_btn2: "Prodloužit",
            mock_tpl_title: "Šablony", mock_fin_title: "Finance", mock_fin_th1: "Zaměstnanec", mock_fin_th2: "Hodiny", mock_fin_th3: "Zálohy", mock_fin_th4: "K výplatě", mock_fin_calc_title: "Výpočet: Ivan Petrenko", mock_fin_calc_h: "Hodiny:", mock_fin_calc_r: "Sazba:", mock_fin_calc_t: "Celkem:",
            mock_rep_title: "Reporty", mock_inv_title: "Faktury", mock_inv_btn: "Generovat PDF", mock_inv_stamp: "ZAPLACENO", mock_inv_total: "Celkem: 25 000 CZK",
            mock_ai_title: "AI Asistent", mock_ai_msg1: "Analyzuj smlouvu zaměstnance", mock_ai_msg2: "Smlouva odpovídá standardům. Věnujte pozornost bodu 4.2 o zkušební době.", mock_ai_msg3: "Jaké dokumenty jsou potřeba pro vízum?",
            mock_arch_th2: "Datum archivace", mock_arch_th3: "Důvod", mock_arch_r1: "Ukončení smlouvy", mock_arch_r2: "Na vlastní žádost", mock_arch_r3: "Přechod k jiné firmě",
            mock_comp_title: "Firmy", mock_sec_title: "Bezpečnost", mock_sec_pass: "Pas: EX12345",
            mock_tg_title: "Agency Contractor Bot", mock_tg_date: "Dnes", mock_tg_voice: "\"Najdi Jana Nováka\"", mock_tg_msg1: "👤 <b>Jan Novák</b><br>Stav: Aktivní ✅<br>Firma: Skoda Auto<br>Telefon: +420 123 456 789<br>Dokumenty: Pas do 12.2026", mock_tg_msg2: "A jaký má plat za květen?", mock_tg_msg3: "💰 <b>Plat: Květen 2026</b><br>Hodiny: 180<br>Sazba: 150 CZK<br>Zálohy: -5 000 CZK<br><b>K výplatě: 22 000 CZK</b>",
            name_ivan: "Jan Novák", name_maria: "Jana Dvořáková", name_oleg: "Petr Svoboda"
        },
        
        ru: {
            nav_features: "Функции", nav_employees: "Сотрудники", nav_templates: "Шаблоны", nav_finance: "Финансы", nav_invoices: "Счета", nav_ai: "AI",
            hero_title: "Сотрудники, документы, зарплаты и AI<br><span class='gradient-text'>в одной системе</span>",
            hero_subtitle: "Agency Contractor заменяет Excel, Word, папки и ручной контроль сроков единой программой для кадровых агентств, работодателей, HR и бухгалтерии.",
            hero_btn_download: "Скачать бесплатно (14 дней)", hero_btn_features: "Смотреть функции",
            hero_proof_1: "Локальная или Сетевая БД", hero_proof_2: "Telegram-бот руководителя", hero_proof_3: "14 дней бесплатно",
            stat_modules: "Модулей", stat_languages: "Языка", stat_templates: "Шаблонов",
            outcome_tag: "Результат", outcome_title: "Вместо хаоса — контроль над каждым процессом", outcome_subtitle: "Программа не просто хранит данные. Она показывает, что нужно сделать, где есть риски и какие цифры важны сегодня.",
            outcome_1_title: "Меньше ручной работы", outcome_1_desc: "AI-сканирование документов, шаблоны, автозаполнение и массовая генерация убирают рутину.",
            outcome_2_title: "Контроль документов", outcome_2_desc: "Паспорт, виза, страховка, разрешение на работу и собственные документы контролируются автоматически.",
            outcome_3_title: "Финансы без путаницы", outcome_3_desc: "Зарплаты, авансы, долги, расходы фирм, PDF/Excel экспорты и итоги за месяц в одном месте.",
            outcome_4_title: "Управление с телефона", outcome_4_desc: "Telegram-бот отвечает на вопросы по сотрудникам, зарплатам, документам и аналитике голосом или текстом.",
            chaos_1: "Документы", chaos_2: "Сотрудники", chaos_3: "Зарплаты", chaos_4: "AI + Telegram",
            feat_tag: "Возможности", feat_title: "Всё для управления агентством", feat_subtitle: "18 модулей, покрывающих каждый аспект работы кадрового агентства",
            feat_dash_title: "Дашборд", feat_dash_desc: "Интерактивная панель с виджетами, статистикой, AI-отчетами и гибкой компоновкой",
            feat_emp_title: "Сотрудники", feat_emp_desc: "Полный цикл: от создания до архивации, с AI-сканированием документов",
            feat_tpl_title: "Шаблоны", feat_tpl_desc: "DOCX, XLSX, PDF редакторы с каталогом тегов и AI-вставкой переменных",
            feat_fin_title: "Финансы", feat_fin_desc: "Зарплатные таблицы, авансы, расходы, экспорт — помесячный учет",
            feat_rep_title: "Отчеты", feat_rep_desc: "Аналитика по фирмам, агентствам, сотрудникам с графиками и PDF/Excel экспортом",
            feat_inv_title: "Счета", feat_inv_desc: "Фактуры, ценовые предложения, кассовые ордера с QR-платежами и ARES",
            feat_ai_title: "AI-ассистент", feat_ai_desc: "Встроенный чат Gemini со специализацией на кадровом праве и документах",
            feat_sec_title: "Безопасность", feat_sec_desc: "Шифрование AES/DPAPI, пароли PBKDF2, подтверждение для опасных действий",
            feat_tg_title: "Telegram-бот", feat_tg_desc: "Управление агентством с телефона: поиск, зарплаты, аналитика и голосовой AI-помощник",
            tg_tag: "Мобильность", tg_title: "Telegram-бот руководителя", tg_desc: "Мгновенный доступ ко всей базе агентства прямо с вашего телефона — без необходимости открывать компьютер.",
            tg_f1: "<strong>Голосовой AI-поиск</strong> — просто скажите боту голосовое сообщение, он распознает речь и найдет сотрудника, фирму или отчет.",
            tg_f2: "<strong>Умный контекст</strong> — бот помнит разговор. Спросите \"Найди Олега\", а затем просто напишите \"А какие у него авансы?\".",
            tg_f3: "<strong>Финансы в кармане</strong> — зарплаты за любой месяц, сводка по фирмам, сравнение месяцев (дельта) и топ выплат.",
            tg_f4: "<strong>Контроль документов</strong> — быстрый запрос списка документов, истекающих через 7 или 30 дней.",
            tg_f5: "<strong>Аналитика кадров</strong> — статистика принятых/уволенных за месяц и чистый баланс текучести кадров.",
            tg_f6: "<strong>HR Новости</strong> — получайте актуальные обновления о миграции и законодательстве прямо в чат.",
            sec_tag: "Архитектура", sec_title: "Гибкая архитектура и Командная работа", sec_desc: "Программа адаптируется под масштабы вашего бизнеса: от автономной работы на одном компьютере до полноценной сетевой системы для всей команды.",
            sec_f1_title: "Для одного ПК (SQLite)", sec_f1_desc: "— работайте максимально быстро с локальной базой данных. Все данные хранятся исключительно на вашем компьютере.",
            sec_f2_title: "Синхронизация (OneDrive)", sec_f2_desc: "— разместите файл базы и документы в облачном хранилище, чтобы иметь доступ к рабочему пространству с разных ПК по очереди.",
            sec_f3_title: "Командная работа (PostgreSQL)", sec_f3_desc: "— объедините 3–5+ пользователей. Разверните базу на главном ПК или сервере и подключайте других сотрудников через LAN или Tailscale.",
            sec_f4_title: "Live Sync (Мгновенная синхронизация)", sec_f4_desc: "— при работе через PostgreSQL все изменения между ПК передаются мгновенно. Другие пользователи видят обновления без задержек.",
            more_tag: "Дополнительно", more_title: "Больше чем просто база данных", more_subtitle: "Еще 9 функций, которые делают вашу ежедневную работу быстрее и приятнее",
            mf_1_title: "Редактор изображений", mf_1_desc: "Обрезка, поворот и сжатие фотографий и сканов прямо в программе.",
            mf_2_title: "Авто-бекапы", mf_2_desc: "Создание резервных копий перед важными изменениями. Откат в 1 клик.",
            mf_3_title: "Мультиязычность", mf_3_desc: "Интерфейс программы доступен на украинском, чешском, английском и русском.",
            mf_4_title: "Массовые операции", mf_4_desc: "Генерируйте договоры для 50 сотрудников одновременно одним нажатием.",
            mf_5_title: "Глобальный поиск", mf_5_desc: "Ищите сотрудников, фирмы или документы с любого экрана (Ctrl+F).",
            mf_6_title: "Журнал активности", mf_6_desc: "Программа записывает каждое действие: кто, когда и что изменил или удалил.",
            mf_7_title: "Темы и цвета", mf_7_desc: "Светлая и темная темы, выбор акцентного цвета под ваше настроение.",
            mf_8_title: "HR Новости", mf_8_desc: "Встроенная лента новостей трудового законодательства с AI-переводом.",
            mf_9_title: "Авто-обновления", mf_9_desc: "Программа сама скачивает новые версии в фоновом режиме (Velopack).",
            pricing_tag: "Тарифы", pricing_title: "Прозрачное ценообразование", pricing_subtitle: "Выберите план, который лучше всего подходит для вашего бизнеса. Без скрытых платежей.",
            price_std_title: "Стандарт", price_year: " CZK / год", price_std_desc: "Полный функционал для управления агентством без использования искусственного интеллекта.",
            price_f_emp_unlim: "Неограниченно сотрудников", price_f_docs: "Генерация всех документов", price_f_fin_inv: "Финансы и Счета", price_f_updates: "Бесплатные обновления", price_f_no_ai: "Без AI-функций",
            price_btn_start: "Начать 14 дней бесплатно", price_recommended: "Рекомендуем", price_ult_title: "Ultimate AI", price_ult_desc: "Максимальная автоматизация благодаря интеграции с Google Gemini.",
            price_f_all_std: "Всё из тарифа \"Стандарт\"", price_f_ai_scan: "AI-сканирование документов", price_f_ai_chat: "Встроенный AI-ассистент (Чат)", price_f_ai_tags: "AI-автозаполнение шаблонов", price_f_ai_reports: "Автоматические AI-отчеты",
            integrations_title: "Работает с вашими любимыми технологиями",
            included_title: "Все тарифы включают:",
            inc_local: "Локальное хранение данных (AES-256)",
            inc_updates: "Бесплатные обновления",
            inc_cloud: "Поддержка OneDrive / Google Drive",
            inc_lang: "Мультиязычность (UK, CS, EN, RU)",
            inc_log: "Журнал активности",
            inc_backup: "Автоматические бекапы",
            roi_tag: "Эффективность", roi_title: "Окупается за первую неделю", roi_desc: "Посмотрите, сколько времени вы теряете на рутину каждый месяц (из расчета на 50 сотрудников), и как Agency Contractor это меняет.",
            roi_without: "Без программы (Excel + Word)", roi_time1: "~35 часов", roi_tasks1: "Ручной ввод данных, поиск ошибок, контроль сроков",
            roi_with: "С Agency Contractor", roi_time2: "~2 часа", roi_tasks2: "AI сканирование, автогенерация в 1 клик, автоматические уведомления",
            roi_save_num: "94%", roi_save_text: "Сэкономленного времени", roi_save_sub: "на каждого HR-менеджера",
            faq_title: "Частые вопросы", faq_q1: "Безопасны ли мои данные?", faq_a1: "Абсолютно. Ваши документы остаются под вашим контролем. Все секреты надежно шифруются с помощью Windows DPAPI, а доступ к командной базе защищен логином и паролем PostgreSQL.",
            faq_q2: "Нужен ли интернет для работы?", faq_a2: "Основной функционал программы работает полностью офлайн. Интернет нужен только для функций AI (Google Gemini), интеграции с ARES (реестр Чехии) и проверки лицензии.",
            faq_q3: "Как работает 14-дневный тестовый период?", faq_a3: "Вы скачиваете программу и получаете полный доступ ко всем функциям (уровень \"Безлимит\") на 14 дней. Никакой привязки карты. После завершения периода вы сможете выбрать удобный тариф.",
            faq_q4: "Почему Windows или антивирус блокирует файл?", faq_a4: "Поскольку программа новая и разработчик еще не имеет накопленного \"рейтинга доверия\" от Microsoft (SmartScreen), система может предупредить о неизвестном файле. Это абсолютно безопасно. При загрузке разрешите сохранение файла, а при запуске нажмите \"Подробнее\" (More info) и \"Выполнить в любом случае\" (Run anyway).",
            faq_q5: "Какой именно файл нужно скачивать?", faq_a5: "На странице GitHub Releases всегда выбирайте самую новую версию (отметка Latest). Для установки программы вам нужен файл с названием <strong>AgencyContractor-win-Setup.exe</strong>.", faq_q6: "На каких компьютерах работает программа?", faq_a6: "Программа оптимизирована для Windows 10 и Windows 11. Для комфортной работы достаточно обычного офисного компьютера (минимум 4 ГБ оперативной памяти).", faq_q7: "Как работает техническая поддержка?", faq_a7: "Мы предоставляем поддержку через Telegram. Также в программе есть встроенная база знаний и пошаговые инструкции.", social_proof: "«Один из наших клиентов с базой в 200+ активных сотрудников сократил время на ежемесячный расчет зарплат, высчет авансов и генерацию фактур с 4-х дней до 3-х часов.»" ,
            replace_tag: "Экосистема", replace_title: "Заменяет хаос на систему", replace_subtitle: "Зачем платить за 5 разных сервисов и путаться в окнах? Всё необходимое уже здесь.", replace_all: "Всё в одном месте",
            perfect_tag: "Для кого", perfect_title: "Идеально подходит для вашего бизнеса", perfect_subtitle: "Программа адаптируется под различные модели работы с персоналом",
            perfect_c1_title: "Кадровые агентства", perfect_c1_1: "Массовый наём и учет", perfect_c1_2: "Контроль сроков виз и разрешений", perfect_c1_3: "Распределение по фирмам-клиентам", perfect_c1_4: "Генерация фактур за услуги",
            perfect_c2_title: "Прямые работодатели", perfect_c2_1: "Заводы, склады, производства", perfect_c2_2: "Учет часов и смен", perfect_c2_3: "Автоматизация договоров", perfect_c2_4: "Надежный локальный архив",
            perfect_c3_title: "HR и Бухгалтеры", perfect_c3_1: "Зарплатные ведомости без ошибок", perfect_c3_2: "Быстрый расчет авансов", perfect_c3_3: "AI-помощник по кадровым вопросам", perfect_c3_4: "Экспорт отчетов в Excel/PDF",
            roi_emp_label: "Количество сотрудников:", roi_bad_title: "Без программы (Потери времени)", roi_hours: "час/мес", roi_bad_desc: "Ручной ввод, поиск ошибок, бумаги", roi_good_title: "С Agency Contractor", roi_good_desc: "Фиксированная цена 10 000 CZK/год. Безлимит сотрудников, AI автоматизация, генерация в 1 клик.", roi_saved_text: "Экономия:",
            roi_bd_docs: "📄 Документы:", roi_bd_salary: "💰 Зарплаты:", roi_bd_invoices: "🧾 Фактуры:", roi_bd_errors: "⚠️ Ошибки:",
            marquee_1: "✨ Powered by Google Gemini AI", marquee_2: "🔒 Защита DPAPI и PostgreSQL", marquee_3: "⚡ Live Sync и OneDrive", marquee_4: "🏢 ARES Интеграция", marquee_5: "⚡ Генерация в 1 клик",
            toast_1: "⚡ AI только что сгенерировал 15 договоров", toast_2: "✅ Зарплаты для 50 сотрудников рассчитаны за 1.2 сек", toast_3: "🔒 Резервная копия успешно создана", toast_4: "📄 Новый шаблон добавлен в базу", toast_5: "🔍 ARES обновил данные 3 компаний", toast_time: "только что",
            sticky_cta_text: "Готовы оптимизировать работу?", sticky_cta_btn: "Начать бесплатно",
            stat_1_title: "Меньше ручной работы", stat_2_title: "Дней бесплатно", stat_3_title: "Языка интерфейса", stat_4_title: "Единая база",
            dash_tag: "Обзор", dash_title: "Интерактивный дашборд", dash_desc: "Центр управления с полной картиной состояния вашего агентства в реальном времени", dash_f1: "<strong>Статистические виджеты</strong> — количество сотрудников, проблем, шаблонов, компаний с трендами", dash_f2: "<strong>Просроченные документы</strong> — список с приоритизацией: просроченные → критические → предупреждения", dash_f3: "<strong>Зарплатная панель</strong> — итоги по месяцам: gross, net, выплачено, авансы", dash_f4: "<strong>Статистика компаний</strong> — сравнение фирм по количеству сотрудников и проблем", dash_f5: "<strong>AI-отчет</strong> — автоматическая аналитическая записка через Google Gemini", dash_f6: "<strong>Гибкая компоновка</strong> — drag & drop виджетов, изменение пропорций, сохранение layout",
            emp_tag: "Ядро системы", emp_title: "Управление сотрудниками", emp_desc: "Полный цикл работы с персоналом — от создания до архивации", emp_f1: "<strong>Пошаговый wizard</strong> — 9 шагов: документы, фото, паспорт, страховка, виза, разрешение, адреса. Адаптируется под тип сотрудника", emp_f2: "<strong>AI-сканирование</strong> — автоматическое распознавание данных из документов через Gemini на каждом шаге", emp_f3: "<strong>Гибкие отчеты</strong> — настраивайте отображение колонок под себя: как в таблицах учета, так и при экспорте", emp_f4: "<strong>Детализированное досье</strong> — номер и орган выдачи визы, гражданство, даты документов, актуальная фирма, агентство и статус", emp_f5: "<strong>Массовые операции</strong> — мультивыбор, пакетная генерация документов, экспорт в Excel", emp_f6: "<strong>Профиль</strong> — 4 вкладки (документы, профиль, история, зарплата), индикатор заполненности, предупреждения о сроках",
            doc_tag: "Контроль", doc_title: "Документы и мониторинг", doc_desc: "Защита от штрафов миграционной полиции. Один просроченный документ может стоить десятки тысяч крон. Программа заранее подсветит сроки.", doc_f1: "<strong>Паспорт, виза, страховка, разрешение</strong> — замена, просмотр, продление с диалогами", doc_f2: "<strong>Кастомные документы</strong> — собственные подписанные документы с датой подписи и сроком действия", doc_f3: "<strong>3 уровня серьезности</strong> — Warning, Critical, Expired с цветовыми индикаторами", doc_f4: "<strong>Агрегация по всем фирмам</strong> — единый экран для всех проблем всех компаний", doc_f5: "<strong>Snooze</strong> — игнорирование проблемы на 7/14/30/60/90 дней с восстановлением", doc_f6: "<strong>PDF-отчет</strong> — экспорт проблем с цветовой маркировкой и статистикой",
            tpl_tag: "Автоматизация", tpl_title: "Шаблоны документов", tpl_desc: "Создавайте документы за секунды — редакторы для каждого формата", tpl_f1: "<strong>DOCX-редактор</strong> — встроенный RTF-редактор с форматированием, каталогом тегов, настройкой страницы", tpl_f2: "<strong>XLSX-редактор</strong> — табличный просмотр с объединенными ячейками и вставкой тегов", tpl_f3: "<strong>PDF-редактор</strong> — визуальное размещение тегов drag & drop с настройкой шрифта и позиции", tpl_f4: "<strong>AI-вставка тегов</strong> — Gemini автоматически размещает переменные в правильные места", tpl_f5: "<strong>Стартовые шаблоны</strong> — каталог готовых шаблонов для быстрого старта", tpl_f6: "<strong>Копирование между фирмами</strong> — перенос шаблона в другую компанию одним кликом",
            fin_tag: "Учет", fin_title: "Финансы и зарплаты", fin_desc: "Помесячный учет зарплат, авансов и расходов с полным контролем", fin_f1: "<strong>Зарплатная таблица</strong> — gross, net, часы, авансы, статус оплаты по каждому сотруднику", fin_f2: "<strong>Работа по разным фирмах</strong> — программа легко обрабатывает сценарии, когда сотрудник работал на нескольких фирмах в одном месяце", fin_f3: "<strong>Авансы</strong> — добавление с названием, суммой, заметкой; авансовые PDF-таблицы по фирмам", fin_f4: "<strong>Расходы фирмы</strong> — отдельный учет расходов компании", fin_f5: "<strong>Командная стабильность</strong> — надежная защита от дублирования истории выплат и минимизация конфликтов при одновременной работе", fin_f6: "<strong>Подписные листы</strong> — PDF с подписями по месяцам, landscape A4",
            rep_tag: "Аналитика", rep_title: "Отчеты и аналитика", rep_desc: "Глубокая аналитика с графиками, фильтрами и экспортом", rep_f1: "<strong>Диапазон дат</strong> — гибкий выбор периода с автоматическим сохранением", rep_f2: "<strong>Фильтры по фирмам и агентствам</strong> — включение/исключение с toggle all", rep_f3: "<strong>Метрики</strong> — активные, новые, завершили, архивированы, восстановлены за период", rep_f4: "<strong>График архива</strong> — помесячная динамика архиваций и восстановлений (OxyPlot)", rep_f5: "<strong>Два режима</strong> — сводный отчет или детальный список сотрудников", rep_f6: "<strong>Экспорт</strong> — выбор листов (фирмы, агентства, сотрудники, архив) в XLSX или PDF",
            inv_tag: "Бухгалтерия", inv_title: "Счета-фактуры", inv_desc: "Полный модуль выставления счетов с QR-платежами и PDF-шаблонами", inv_f1: "<strong>5 типов документов</strong> — счет, ценовое предложение, заказ, приходный и расходный кассовые ордера", inv_f2: "<strong>ARES-интеграция</strong> — автозаполнение реквизитов по IČO из чешского реестра", inv_f3: "<strong>QR-платежи</strong> — форматы SPAYD / EPC с живым превью кода", inv_f4: "<strong>6 PDF-шаблонов + 4 темы</strong> — стилизованные счета с логотипом и печатью", inv_f5: "<strong>4 валюты</strong> — CZK, EUR, USD, PLN со скидками и частичной оплатой", inv_f6: "<strong>Каталоги</strong> — сохраненные поставщики, заказчики, товары для быстрого заполнения",
            ai_tag: "Искусственный интеллект", ai_title: "AI-интеграция Gemini", ai_desc: "Google Gemini встроен в каждый аспект программы", ai_f1: "<strong>AI-чат</strong> — специализированный ассистент для кадровых агентств: трудовое право, документы, разрешения", ai_f2: "<strong>Сканирование документов</strong> — автоматическое распознавание данных из паспортов, виз, страховок", ai_f3: "<strong>AI-поиск</strong> — нахождение сотрудников на естественном языке", ai_f4: "<strong>AI-вставка тегов</strong> — автоматическое размещение переменных в шаблонах", ai_f5: "<strong>AI-отчет дашборда</strong> — аналитическая записка по всем метрикам", ai_f6: "<strong>Новости с AI</strong> — перевод, анализ, вопросы к статьям новостной ленты",
            arch_tag: "Защита данных", arch_title: "Архив и мягкое удаление", arch_desc: "Никакие данные не теряются — полный контроль над жизненным циклом сотрудника", arch_f1: "<strong>Архив</strong> — отдельный процесс завершения сотрудничества с датой; поиск, фильтры, восстановление", arch_f2: "<strong>Недавно удаленные</strong> — мягкое удаление с возможностью восстановления, архивации или окончательного удаления", arch_f3: "<strong>Отмена действий</strong> — кнопка Undo для недавних архиваций с подтверждением пароля", arch_f4: "<strong>Просмотр профиля</strong> — read-only доступ к профилю удаленных и архивных сотрудников", arch_f5: "<strong>История действий</strong> — полный журнал всех операций с фильтрами, категориями, экспортом в Excel", arch_f6: "<strong>Умный Backup</strong> — ежедневное копирование в Backup\\YYYY-MM-DD (7 дней). В командном режиме автоматический экспорт PostgreSQL → SQLite",
            comp_tag: "Организация", comp_title: "Компании и кандидаты", comp_desc: "Управление фирмами-работодателями и базой кандидатов", comp_f1: "<strong>Фирмы-работодатели</strong> — название, IČO, юр. адрес, до 4 рабочих адресов, позиции с зарплатами", comp_f2: "<strong>Агентство</strong> — привязка агентства (название, IČO, адрес) к каждой фирме", comp_f3: "<strong>График работы</strong> — недельные/дневные часы, количество смен", comp_f4: "<strong>Видимость</strong> — скрытие фирм без удаления; проверка на активных сотрудников", comp_f5: "<strong>Кандидаты</strong> — отдельная база с профилями, фото, документами; поиск по позиции", comp_f6: "<strong>Глобальный поиск</strong> — нахождение сотрудников, шаблонов, кандидатов, архива из одной строки",
            start_tag: "Быстрый старт", start_title: "Начать можно за несколько минут", start_subtitle: "Установите программу, создайте профиль и постепенно перенесите работу из Excel, Word и папок в единую систему.",
            start_1_title: "Скачайте Setup.exe", start_1_desc: "Последняя версия всегда доступна на GitHub Releases с автообновлением.",
            start_2_title: "Добавьте фирму", start_2_desc: "Укажите реквизиты, адреса, позиции, график работы и агентство.",
            start_3_title: "Создайте сотрудника", start_3_desc: "Wizard, AI-сканирование документов, фото, адреса, история и контроль сроков.",
            start_4_title: "Подключите AI или Telegram", start_4_desc: "Получайте ответы по базе, зарплатам, документам и отчетам прямо из программы или телефона.",
            cta_title: "Готовы оптимизировать<br>работу вашего агентства?", cta_desc: "Agency Contractor — всё необходимое для кадрового агентства в одной программе", cta_btn: "Начать бесплатный период", cta_f1: "4 языка", cta_f2: "Шифрование",
            mock_dash_title: "Дашборд", mock_dash_crit: "Критические документы", mock_emp_title: "Сотрудники", mock_emp_scan: "AI Сканирование документа",
            mock_doc_title: "Документы", mock_doc_th1: "Документ", mock_doc_th2: "Сотрудник", mock_doc_th3: "Статус", mock_doc_th4: "Действие", mock_doc_pass: "Паспорт", mock_doc_visa: "Виза", mock_doc_ins: "Страховка", mock_doc_st_exp: "Просрочено", mock_doc_st_warn: "Внимание", mock_doc_st_ok: "Ок", mock_doc_pop_title: "Внимание: Документ просрочен!", mock_doc_pop_desc: "Паспорт Ивана Петренко истек 2 дня назад.", mock_doc_pop_btn1: "Игнорировать", mock_doc_pop_btn2: "Продлить",
            mock_tpl_title: "Шаблоны", mock_fin_title: "Финансы", mock_fin_th1: "Сотрудник", mock_fin_th2: "Часы", mock_fin_th3: "Авансы", mock_fin_th4: "К выплате", mock_fin_calc_title: "Расчет: Иван Петренко", mock_fin_calc_h: "Часы:", mock_fin_calc_r: "Ставка:", mock_fin_calc_t: "Итого:",
            mock_rep_title: "Отчеты", mock_inv_title: "Счета", mock_inv_btn: "Сгенерировать PDF", mock_inv_stamp: "PAID", mock_inv_total: "Итого: 25 000 CZK",
            mock_ai_title: "AI Ассистент", mock_ai_msg1: "Проанализируй договор сотрудника", mock_ai_msg2: "Договор соответствует стандартам. Обратите внимание на пункт 4.2 об испытательном сроке.", mock_ai_msg3: "Какие документы нужны для визы?",
            mock_arch_th2: "Дата архивации", mock_arch_th3: "Причина", mock_arch_r1: "Окончание контракта", mock_arch_r2: "По собственному желанию", mock_arch_r3: "Переход на другую фирму",
            mock_comp_title: "Компании", mock_sec_title: "Безопасность", mock_sec_pass: "Паспорт: EX12345",
            mock_tg_title: "Agency Contractor Bot", mock_tg_date: "Сегодня", mock_tg_voice: "\"Найди Ивана Петренко\"", mock_tg_msg1: "👤 <b>Иван Петренко</b><br>Статус: Активный ✅<br>Фирма: Skoda Auto<br>Телефон: +420 123 456 789<br>Документы: Паспорт до 12.2026", mock_tg_msg2: "А какая у него зарплата за май?", mock_tg_msg3: "💰 <b>Зарплата: Май 2026</b><br>Часы: 180<br>Ставка: 150 CZK<br>Авансы: -5 000 CZK<br><b>К выплате: 22 000 CZK</b>",
            name_ivan: "Иван Петренко", name_maria: "Мария Коваль", name_oleg: "Олег Сидор"
        }
,
        en: {
            nav_features: "Features", nav_employees: "Employees", nav_templates: "Templates", nav_finance: "Finance", nav_invoices: "Invoices", nav_ai: "AI",
            hero_title: "Employees, documents, payroll and AI<br><span class='gradient-text'>in one system</span>",
            hero_subtitle: "Agency Contractor replaces Excel, Word, folders and manual deadline tracking with one app for staffing agencies, employers, HR and accounting.",
            hero_btn_download: "Download Free (14 days)", hero_btn_features: "View Features",
            hero_proof_1: "Local or Network DB", hero_proof_2: "Telegram bot for managers", hero_proof_3: "14 days free",
            stat_modules: "Modules", stat_languages: "Languages", stat_templates: "Templates",
            outcome_tag: "Outcome", outcome_title: "From chaos to control over every process", outcome_subtitle: "The app does not just store data. It shows what needs attention, where risks exist and which numbers matter today.",
            outcome_1_title: "Less manual work", outcome_1_desc: "AI document scanning, templates, autofill and batch generation remove repetitive tasks.",
            outcome_2_title: "Document control", outcome_2_desc: "Passport, visa, insurance, work permit and custom documents are monitored automatically.",
            outcome_3_title: "Finance without confusion", outcome_3_desc: "Payroll, advances, debts, company expenses, PDF/Excel exports and monthly totals in one place.",
            outcome_4_title: "Manage from your phone", outcome_4_desc: "The Telegram bot answers questions about employees, payroll, documents and analytics by voice or text.",
            chaos_1: "Documents", chaos_2: "Employees", chaos_3: "Payroll", chaos_4: "AI + Telegram",
            feat_tag: "Capabilities", feat_title: "Everything for agency management", feat_subtitle: "18 modules covering every aspect of a staffing agency's work",
            feat_dash_title: "Dashboard", feat_dash_desc: "Interactive panel with widgets, statistics, AI reports, and flexible layout",
            feat_emp_title: "Employees", feat_emp_desc: "Full cycle: from wizard creation to archiving, with AI document scanning",
            feat_tpl_title: "Templates", feat_tpl_desc: "DOCX, XLSX, PDF editors with tag catalog and AI variable insertion",
            feat_fin_title: "Finance", feat_fin_desc: "Salary tables, advances, expenses, export — monthly accounting",
            feat_rep_title: "Reports", feat_rep_desc: "Analytics for companies, agencies, employees with charts and PDF/Excel export",
            feat_inv_title: "Invoices", feat_inv_desc: "Invoices, quotes, cash receipts with QR payments and ARES",
            feat_ai_title: "AI Assistant", feat_ai_desc: "Built-in Gemini chat specializing in labor law and documents",
            feat_sec_title: "Security", feat_sec_desc: "AES/DPAPI encryption, PBKDF2 passwords, confirmation for dangerous actions",
            feat_tg_title: "Telegram Bot", feat_tg_desc: "Agency management from your phone: search, salaries, analytics, and voice AI assistant",
            tg_tag: "Mobility", tg_title: "Telegram Bot for Managers", tg_desc: "Instant access to the entire agency database right from your phone — without needing to open your computer.",
            tg_f1: "<strong>Voice AI search</strong> — just send a voice message, the bot recognizes speech and finds the employee, company, or report.",
            tg_f2: "<strong>Smart context</strong> — the bot remembers the conversation. Ask \"Find Oleg\" and then just type \"What are his advances?\".",
            tg_f3: "<strong>Finance in your pocket</strong> — salaries for any month, company summaries, month comparisons (delta), and top payouts.",
            tg_f4: "<strong>Document control</strong> — quick request for a list of documents expiring in 7 or 30 days.",
            tg_f5: "<strong>HR Analytics</strong> — statistics of hired/fired per month and net balance of staff turnover.",
            tg_f6: "<strong>HR News</strong> — get the latest updates on migration and legislation directly in the chat.",
            sec_tag: "Architecture", sec_title: "Flexible Architecture & Teamwork", sec_desc: "We do not have access to your database. All data is stored locally and protected by banking system standards (GDPR Compliant).",
            sec_f1_title: "Local Storage", sec_f1_desc: "— the database is only on your PC. No leaks to the internet.",
            sec_f2_title: "Synchronization (OneDrive)", sec_f2_desc: "— database files are encrypted. Even if someone steals the file, they cannot read it.",
            sec_f3_title: "Teamwork (PostgreSQL)", sec_f3_desc: "— connect 3–5+ users. Deploy the database on a main PC or server and connect other employees via LAN or Tailscale.",
            sec_f4_title: "Live Sync (Instant sync)", sec_f4_desc: "— when working through PostgreSQL, all changes between PCs are transmitted instantly. Other users see updates without delays.",
            
            more_tag: "More", more_title: "More than just a database", more_subtitle: "9 more features that make your daily work faster and more pleasant",
            mf_1_title: "Image Editor", mf_1_desc: "Crop, rotate, and compress photos and scans directly in the program.",
            mf_2_title: "Auto-backups", mf_2_desc: "Create backups before important changes. 1-click rollback.",
            mf_3_title: "Multilingual", mf_3_desc: "Program interface available in Ukrainian, Czech, English, and Russian.",
            mf_4_title: "Bulk Operations", mf_4_desc: "Generate contracts for 50 employees at once with a single click.",
            mf_5_title: "Global Search", mf_5_desc: "Search for employees, companies, or documents from any screen (Ctrl+F).",
            mf_6_title: "Activity Log", mf_6_desc: "The program records every action: who, when, and what was changed or deleted.",
            mf_7_title: "Themes and Colors", mf_7_desc: "Light and dark themes, choice of accent color to suit your mood.",
            mf_8_title: "HR News", mf_8_desc: "Built-in labor law news feed with AI translation.",
            mf_9_title: "Auto-updates", mf_9_desc: "The program automatically downloads new versions in the background (Velopack).",

            pricing_tag: "Pricing", pricing_title: "Transparent Pricing", pricing_subtitle: "Choose the plan that best fits your business. No hidden fees.",
            price_std_title: "Standard", price_year: " CZK / year", price_std_desc: "Full functionality for agency management without using artificial intelligence.",
            price_f_emp_unlim: "Unlimited employees", price_f_docs: "Generation of all documents", price_f_fin_inv: "Finance and Invoices", price_f_updates: "Free updates", price_f_no_ai: "No AI features",
            price_btn_start: "Start 14 days free", price_recommended: "Recommended", price_ult_title: "Ultimate AI", price_ult_desc: "Maximum automation thanks to integration with Google Gemini.",
            price_f_all_std: "Everything from \"Standard\" plan", price_f_ai_scan: "AI document scanning", price_f_ai_chat: "Built-in AI assistant (Chat)", price_f_ai_tags: "AI auto-filling templates", price_f_ai_reports: "Automatic AI reports",
            integrations_title: "Works with your favorite technologies",
            included_title: "All plans include:",
            inc_local: "Local data storage (AES-256)",
            inc_updates: "Free updates",
            inc_cloud: "OneDrive / Google Drive support",
            inc_lang: "Multilingual (UK, CS, EN)",
            inc_log: "Activity log",
            inc_backup: "Automatic backups",
            roi_tag: "Efficiency", roi_title: "Pays off in the first week", roi_desc: "See how much time you lose on routine every month (based on 50 employees), and how Agency Contractor changes it.",
            roi_without: "Without app (Excel + Word)", roi_time1: "~35 hours", roi_tasks1: "Manual data entry, finding errors, deadline control",
            roi_with: "With Agency Contractor", roi_time2: "~2 hours", roi_tasks2: "AI scanning, 1-click generation, automatic notifications",
            roi_save_num: "94%", roi_save_text: "Time Saved", roi_save_sub: "for every HR manager",
            faq_title: "Frequently Asked Questions", faq_q1: "Are my data safe?", faq_a1: "Absolutely. Your documents remain under your control. All secrets are securely encrypted using Windows DPAPI, and access to the team database is protected by a PostgreSQL login and password.",
            faq_q2: "Do I need internet to work?", faq_a2: "The core functionality of the program works completely offline. Internet is only required for AI features (Google Gemini), integration with ARES (Czech registry), and license verification.",
            faq_q3: "How does the 14-day trial work?", faq_a3: "You download the program and get full access to all features (\"Unlimited\" tier) for 14 days. No credit card required. After the period ends, you can choose a suitable plan.",
            faq_q4: "Why does Windows or antivirus block the file?", faq_a4: "Since the program is new and the developer does not yet have a \"trust rating\" from Microsoft (SmartScreen), the system may warn about an unknown file. It is completely safe. Allow the file to be saved during download, and when running, click \"More info\" and \"Run anyway\".",
            faq_q5: "Which file exactly should I download?", faq_a5: "On the GitHub Releases page, always choose the latest version (Latest tag). To install the program, you need the file named <strong>AgencyContractor-win-Setup.exe</strong>.", faq_q6: "What computers does the program work on?", faq_a6: "The program is optimized for Windows 10 and Windows 11. A standard office computer (minimum 4 GB RAM) is sufficient for comfortable work.", faq_q7: "How does technical support work?", faq_a7: "We provide support via Telegram. The program also has a built-in knowledge base and step-by-step instructions.", social_proof: "«One of our clients with a database of 200+ active employees reduced the time for monthly payroll, advances, and invoice generation from 4 days to 3 hours.»" ,
            replace_tag: "Ecosystem", replace_title: "Replaces chaos with system", replace_subtitle: "Why pay for 5 different services and get lost in windows? Everything you need is already here.", replace_all: "All in one place",
            perfect_tag: "For Whom", perfect_title: "Perfect for your business", perfect_subtitle: "The program adapts to different models of personnel management",
            perfect_c1_title: "Staffing Agencies", perfect_c1_1: "Mass hiring and accounting", perfect_c1_2: "Visa and permit deadline control", perfect_c1_3: "Distribution by client companies", perfect_c1_4: "Invoice generation for services",
            perfect_c2_title: "Direct Employers", perfect_c2_1: "Factories, warehouses, production", perfect_c2_2: "Hours and shifts tracking", perfect_c2_3: "Contract automation", perfect_c2_4: "Reliable local archive",
            perfect_c3_title: "HR and Accountants", perfect_c3_1: "Error-free payrolls", perfect_c3_2: "Quick advance calculations", perfect_c3_3: "AI assistant for HR questions", perfect_c3_4: "Export reports to Excel/PDF",
            roi_emp_label: "Number of employees:", roi_bad_title: "Without app (Time lost)", roi_hours: "hrs/mo", roi_bad_desc: "Manual entry, finding errors, papers", roi_good_title: "With Agency Contractor", roi_good_desc: "Fixed price 10,000 CZK/year. Unlimited employees, AI automation, 1-click generation.", roi_saved_text: "Savings:",
            roi_bd_docs: "📄 Documents:", roi_bd_salary: "💰 Salaries:", roi_bd_invoices: "🧾 Invoices:", roi_bd_errors: "⚠️ Errors:",
            marquee_1: "✨ Powered by Google Gemini AI", marquee_2: "🔒 AES-256 Encryption", marquee_3: "☁️ OneDrive Sync", marquee_4: "🏢 ARES Integration", marquee_5: "⚡ 1-Click Generation",
            toast_1: "⚡ AI just generated 15 contracts", toast_2: "✅ Salaries for 50 employees calculated in 1.2s", toast_3: "🔒 Backup successfully created", toast_4: "📄 New template added to database", toast_5: "🔍 ARES updated data for 3 companies", toast_time: "just now",
            sticky_cta_text: "Ready to optimize your work?", sticky_cta_btn: "Start for free",
            stat_1_title: "Less manual work", stat_2_title: "Free trial days", stat_3_title: "Interface languages", stat_4_title: "Single database",
            dash_tag: "Overview", dash_title: "Interactive Dashboard", dash_desc: "Command center with a complete picture of your agency's status in real-time", dash_f1: "<strong>Statistical widgets</strong> — number of employees, problems, templates, companies with trends", dash_f2: "<strong>Expired documents</strong> — prioritized list: expired → critical → warning", dash_f3: "<strong>Payroll panel</strong> — monthly summaries: gross, net, paid, advances", dash_f4: "<strong>Company stats</strong> — comparing companies by employee count and problems", dash_f5: "<strong>AI report</strong> — automated analytical brief via Google Gemini", dash_f6: "<strong>Flexible layout</strong> — drag & drop widgets, resizing, layout saving",
            emp_tag: "System Core", emp_title: "Employee Management", emp_desc: "Full personnel lifecycle — from creation to archiving", emp_f1: "<strong>Step-by-step wizard</strong> — 9 steps: docs, photo, passport, insurance, visa, permit, addresses. Adapts to employee type", emp_f2: "<strong>AI scanning</strong> — automated data recognition from documents via Gemini at each step", emp_f3: "<strong>Flexible reports</strong> — customize columns display for yourself: both in tables and when exporting", emp_f4: "<strong>Detailed dossier</strong> — visa number and issuing authority, citizenship, document dates, current firm, agency, and status", emp_f5: "<strong>Mass operations</strong> — multi-select, batch document generation, Excel export", emp_f6: "<strong>Profile</strong> — 4 tabs (docs, profile, history, salary), completion indicator, deadline warnings",
            doc_tag: "Control", doc_title: "Documents & Monitoring", doc_desc: "Protection against migration police fines. One expired permit can cost your business tens of thousands of crowns. The program highlights critical deadlines in advance.", doc_f1: "<strong>Passport, visa, insurance, permit</strong> — replacement, viewing, extension with dialogs", doc_f2: "<strong>Custom documents</strong> — own signed documents with signature date and validity", doc_f3: "<strong>3 severity levels</strong> — Warning, Critical, Expired with color indicators", doc_f4: "<strong>Global aggregation</strong> — single screen for all problems across all companies", doc_f5: "<strong>Snooze</strong> — ignore problem for 7/14/30/60/90 days with auto-restore", doc_f6: "<strong>PDF report</strong> — problem export with color coding and statistics",
            tpl_tag: "Automation", tpl_title: "Document Templates", tpl_desc: "Create documents in seconds — editors for every format", tpl_f1: "<strong>DOCX editor</strong> — built-in RTF editor with formatting, tag catalog, page setup", tpl_f2: "<strong>XLSX editor</strong> — spreadsheet view with merged cells and tag insertion", tpl_f3: "<strong>PDF editor</strong> — visual drag & drop tag placement with font and position setup", tpl_f4: "<strong>AI tag insertion</strong> — Gemini automatically places variables in correct spots", tpl_f5: "<strong>Starter templates</strong> — catalog of ready-made templates for quick start", tpl_f6: "<strong>Copy between companies</strong> — transfer template to another company in one click",
            fin_tag: "Accounting", fin_title: "Finance & Payroll", fin_desc: "Monthly payroll, advances, and expenses with full control", fin_f1: "<strong>Salary table</strong> — gross, net, hours, advances, payment status per employee", fin_f2: "<strong>Work across multiple firms</strong> — easily handle scenarios where an employee worked for several firms in one month", fin_f3: "<strong>Advances</strong> — add with name, amount, note; advance PDF tables per company", fin_f4: "<strong>Company expenses</strong> — separate company expense tracking", fin_f5: "<strong>Team stability</strong> — reliable protection against payment history duplication and conflict minimization during simultaneous work", fin_f6: "<strong>Signature sheets</strong> — PDF with signatures by month, landscape A4",
            rep_tag: "Analytics", rep_title: "Reports & Analytics", rep_desc: "Deep analytics with charts, filters, and export", rep_f1: "<strong>Date range</strong> — flexible period selection with auto-save", rep_f2: "<strong>Company & agency filters</strong> — include/exclude with toggle all", rep_f3: "<strong>Metrics</strong> — active, new, finished, archived, restored in period", rep_f4: "<strong>Archive chart</strong> — monthly dynamics of archivals and restorations (OxyPlot)", rep_f5: "<strong>Two modes</strong> — summary report or detailed employee list", rep_f6: "<strong>Export</strong> — select sheets (companies, agencies, employees, archive) to XLSX or PDF",
            inv_tag: "Bookkeeping", inv_title: "Invoices", inv_desc: "Full invoicing module with QR payments and PDF templates", inv_f1: "<strong>5 document types</strong> — invoice, quote, order, income and expense cash receipts", inv_f2: "<strong>ARES integration</strong> — auto-fill details by ID from Czech registry", inv_f3: "<strong>QR payments</strong> — SPAYD / EPC formats with live code preview", inv_f4: "<strong>6 PDF templates + 4 themes</strong> — styled invoices with logo and stamp", inv_f5: "<strong>4 currencies</strong> — CZK, EUR, USD, PLN with discounts and partial payment", inv_f6: "<strong>Catalogs</strong> — saved suppliers, customers, items for quick filling",
            ai_tag: "Artificial Intelligence", ai_title: "Gemini AI Integration", ai_desc: "Google Gemini is built into every aspect of the program", ai_f1: "<strong>AI chat</strong> — specialized assistant for staffing agencies: labor law, documents, permits", ai_f2: "<strong>Document scanning</strong> — automated data recognition from passports, visas, insurance", ai_f3: "<strong>AI search</strong> — finding employees using natural language", ai_f4: "<strong>AI tag insertion</strong> — automated variable placement in templates", ai_f5: "<strong>Dashboard AI report</strong> — analytical brief across all metrics", ai_f6: "<strong>AI News</strong> — translation, analysis, questions about news feed articles",
            arch_tag: "Data Protection", arch_title: "Archive & Soft Delete", arch_desc: "No data is lost — full control over employee lifecycle", arch_f1: "<strong>Archive</strong> — separate termination process with date; search, filters, restore", arch_f2: "<strong>Recently deleted</strong> — soft delete with option to restore, archive or permanently delete", arch_f3: "<strong>Undo action</strong> — Undo button for recent archivals with password confirmation", arch_f4: "<strong>Profile viewing</strong> — read-only access to deleted and archived employee profiles", arch_f5: "<strong>Action history</strong> — full log of all operations with filters, categories, Excel export", arch_f6: "<strong>Smart Backup</strong> — daily backups to Backup\YYYY-MM-DD (7 days). In team mode, automatic export PostgreSQL → SQLite",
            comp_tag: "Organization", comp_title: "Companies & Candidates", comp_desc: "Manage client companies and candidate database", comp_f1: "<strong>Client companies</strong> — name, ID, legal address, up to 4 work addresses, positions with salaries", comp_f2: "<strong>Agency</strong> — link agency (name, ID, address) to each company", comp_f3: "<strong>Work schedule</strong> — weekly/daily hours, number of shifts", comp_f4: "<strong>Visibility</strong> — hide companies without deleting; check for active employees", comp_f5: "<strong>Candidates</strong> — separate database with profiles, photo, documents; search by position", comp_f6: "<strong>Global search</strong> — find employees, templates, candidates, archive from one bar",
            start_tag: "Quick start", start_title: "Start in a few minutes", start_subtitle: "Install the app, create a profile and gradually move work from Excel, Word and folders into one system.",
            start_1_title: "Download Setup.exe", start_1_desc: "The latest version is always available on GitHub Releases with automatic updates.",
            start_2_title: "Add a company", start_2_desc: "Enter requisites, addresses, positions, working hours and agency data.",
            start_3_title: "Create an employee", start_3_desc: "Wizard, AI document scanning, photo, addresses, history and deadline control.",
            start_4_title: "Connect AI or Telegram", start_4_desc: "Get answers about your database, payroll, documents and reports directly from the app or phone.",
            cta_title: "Ready to optimize<br>your agency's work?", cta_desc: "Agency Contractor — everything you need for a staffing agency in one app", cta_btn: "Start free trial", cta_f1: "4 languages", cta_f2: "Encryption",
            mock_dash_title: "Dashboard", mock_dash_crit: "Critical documents", mock_emp_title: "Employees", mock_emp_scan: "AI Document Scanning",
            mock_doc_title: "Documents", mock_doc_th1: "Document", mock_doc_th2: "Employee", mock_doc_th3: "Status", mock_doc_th4: "Action", mock_doc_pass: "Passport", mock_doc_visa: "Visa", mock_doc_ins: "Insurance", mock_doc_st_exp: "Expired", mock_doc_st_warn: "Warning", mock_doc_st_ok: "Ok", mock_doc_pop_title: "Warning: Document expired!", mock_doc_pop_desc: "Ivan Petrenko's passport expired 2 days ago.", mock_doc_pop_btn1: "Ignore", mock_doc_pop_btn2: "Extend",
            mock_tpl_title: "Templates", mock_fin_title: "Finance", mock_fin_th1: "Employee", mock_fin_th2: "Hours", mock_fin_th3: "Advances", mock_fin_th4: "To Pay", mock_fin_calc_title: "Calculation: Ivan Petrenko", mock_fin_calc_h: "Hours:", mock_fin_calc_r: "Rate:", mock_fin_calc_t: "Total:",
            mock_rep_title: "Reports", mock_inv_title: "Invoices", mock_inv_btn: "Generate PDF", mock_inv_stamp: "PAID", mock_inv_total: "Total: 25 000 CZK",
            mock_ai_title: "AI Assistant", mock_ai_msg1: "Analyze the employee's contract", mock_ai_msg2: "The contract meets standards. Pay attention to clause 4.2 regarding the probation period.", mock_ai_msg3: "What documents are needed for a visa?",
            mock_arch_th2: "Archive Date", mock_arch_th3: "Reason", mock_arch_r1: "Contract ended", mock_arch_r2: "Own request", mock_arch_r3: "Transfer to another firm",
            mock_comp_title: "Companies", mock_sec_title: "Security", mock_sec_pass: "Passport: EX12345",
            mock_tg_title: "Agency Contractor Bot", mock_tg_date: "Today", mock_tg_voice: "\"Find John Smith\"", mock_tg_msg1: "👤 <b>John Smith</b><br>Status: Active ✅<br>Company: Skoda Auto<br>Phone: +420 123 456 789<br>Docs: Passport until 12.2026", mock_tg_msg2: "And what is his salary for May?", mock_tg_msg3: "💰 <b>Salary: May 2026</b><br>Hours: 180<br>Rate: 150 CZK<br>Advances: -5 000 CZK<br><b>To pay: 22 000 CZK</b>",
            name_ivan: "John Smith", name_maria: "Mary Johnson", name_oleg: "Oliver Brown"
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

    const animatedElements = document.querySelectorAll('.animate-fade-up, .animate-slide-left, .animate-slide-right, .more-feature-card, .stat-counter, .faq-item, .included-features');
    animatedElements.forEach(el => {
        const delay = el.getAttribute('data-delay');
        if (delay) {
            el.style.transitionDelay = `${delay}ms`;
        }
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

    // 4. Magnetic Buttons Effect
    const magneticButtons = document.querySelectorAll('.btn-primary, .btn-secondary');
    magneticButtons.forEach(btn => {
        btn.addEventListener('mousemove', (e) => {
            const rect = btn.getBoundingClientRect();
            const x = e.clientX - rect.left - rect.width / 2;
            const y = e.clientY - rect.top - rect.height / 2;
            
            // Move button slightly towards cursor
            btn.style.transform = `translate(${x * 0.2}px, ${y * 0.2}px)`;
        });
        
        btn.addEventListener('mouseleave', () => {
            // Reset position
            btn.style.transform = `translate(0px, 0px)`;
        });
    });

    // 5. Glow Border Hover Effect for Feature Cards & More Feature Cards
    const featureCards = document.querySelectorAll('.feature-card, .more-feature-card');
    featureCards.forEach(card => {
        card.addEventListener('mousemove', (e) => {
            const rect = card.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;
            
            // Calculate angle for gradient
            const centerX = rect.width / 2;
            const centerY = rect.height / 2;
            const angle = Math.atan2(y - centerY, x - centerX) * 180 / Math.PI;
            
            card.style.setProperty('--mouse-angle', `${angle + 90}deg`);
            card.style.setProperty('--mouse-x', `${x}px`);
            card.style.setProperty('--mouse-y', `${y}px`);
        });
    });

    // 6. FAQ Accordion
    const faqItems = document.querySelectorAll('.faq-item');
    faqItems.forEach(item => {
        const question = item.querySelector('.faq-question');
        const answer = item.querySelector('.faq-answer');
        
        question.addEventListener('click', () => {
            const isActive = item.classList.contains('active');
            
            // Close all other items
            faqItems.forEach(otherItem => {
                otherItem.classList.remove('active');
                otherItem.querySelector('.faq-answer').style.maxHeight = null;
            });
            
            // Toggle current item
            if (!isActive) {
                item.classList.add('active');
                answer.style.maxHeight = answer.scrollHeight + "px";
            }
        });
    });

});
