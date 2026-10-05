import { en, type Translations } from '@docx-editor.dev/i18n';
import { uk } from './uk';
import { ru } from './ru';
import { cs } from './cs';

const catalogs: Record<string, Translations> = { uk, ru, cs, en };

export function catalogFor(languageCode: string): Translations {
  return catalogs[languageCode] ?? en;
}

export function regionalLocaleFor(languageCode: string): string {
  switch (languageCode) {
    case 'uk':
      return 'uk-UA';
    case 'ru':
      return 'ru-RU';
    case 'cs':
      return 'cs-CZ';
    default:
      return 'en-US';
  }
}
