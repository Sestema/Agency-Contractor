# Agency Contractor 0.1.55

У цьому релізі зосередилися на великому Fluent-оновленні інтерфейсу під Windows 11, покращенні тем і акцентів, а також на виправленні кількох реальних поведінкових проблем навколо backdrop, налаштувань і таблиць.

- оновлено візуальну систему застосунку в стилі Windows 11: додано `Segoe UI Variable`, `Segoe Fluent Icons`, semantic type ramp, глобальний focus ring, modern context menu/menu item styles, soft elevation для карток і діалогів, а також покращене згладжування тексту й зображень;
- головне вікно переведено на custom `WindowChrome` з Fluent caption buttons, коректною поведінкою в maximized-стані та явним застосуванням backdrop після `SourceInitialized`, щоб `Mica`/`Acrylic` стабільно вмикались у правильний момент;
- розширено й нормалізовано систему тем: покращено `Light`, `Dark2`, `DarkWord`, `Custom`, `Glass` і `GlassDark`, додано коректний вибір backdrop для кожної теми, а також повноцінний accent color picker у `Settings` з повторним застосуванням акценту поверх активної теми;
- перероблено ключові інтерактивні елементи: `CardControl` отримав reveal-highlight ефект і сучасні hover-анімації, переходи між сторінками стали плавнішими, а контекстні меню, tooltips і toolbar-кнопки приведено до єдиного Fluent-стилю;
- модернізовано таблиці й збереження layout: для salary-таблиці додано live persistence ширини колонок через `DependencyPropertyDescriptor` з debounce, гарантований flush settings при закритті головного вікна і покращені анімації/віртуалізацію для важких сценаріїв;
- додатково прибрано велику кількість hardcoded кольорів у views на користь `DynamicResource`, переведено багато екранів на `PageBackgroundBrush`, виправлено визначення активної теми в `Settings`, і вирівняно поведінку тем, тіней, радіусів та контрасту між світлими й темними режимами.
