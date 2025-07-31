# SimpleImprove Language Support

This mod supports multiple languages through RimWorld's standard localization system.

## Available Languages

All languages include complete translations for:
- UI designators and tooltips
- Gizmo buttons and descriptions  
- Settings menu text
- Success/failure messages
- Skill warnings
- Work type and job descriptions
- Mod name and description

- **English** - Complete (base language)
- **Spanish** - Complete (Español)
- **Japanese** - Complete (日本語)
- **Chinese (Simplified)** - Complete (简体中文)
- **French** - Complete (Français)
- **German** - Complete (Deutsch)
- **Polish** - Complete (Polski)
- **Russian** - Complete (Русский)
- **Portuguese (Brazilian)** - Complete (Português Brasil)

## Adding New Languages

To add support for a new language:

1. Create a new folder in `Languages/` with your language name (e.g., `French`, `German`, `Japanese`)

2. Copy the structure from the `English` folder:
   ```
   YourLanguage/
   ├── LanguageInfo.xml
   ├── About/
   │   └── About.xml
   ├── Keyed/
   │   └── SimpleImprove_Keys.xml
   └── DefInjected/
       ├── JobDef/
       │   └── Jobs_Improve.xml
       └── WorkTypeDef/
           └── WorkTypes_Improve.xml
   ```

3. Update `LanguageInfo.xml` with your language details:
   - `friendlyNameNative`: Your language name in its native script
   - `friendlyNameEnglish`: Your language name in English
   - `languageWorkerClass`: Use appropriate RimWorld language worker class

4. Translate all text strings in the XML files while keeping the same structure and key names

5. Test your translation in-game by switching to your language in RimWorld settings

## Translation Guidelines

- Keep the XML structure and key names exactly the same
- Only translate the text content between the XML tags
- Use `{0}`, `{1}`, etc. placeholders exactly as shown in the English version
- Test translations in-game to ensure they fit the UI properly
- Consider character limits for UI elements like buttons and labels

## Contributing Translations

We welcome community translations! Please:

1. Fork the repository
2. Add your language following the structure above
3. Test thoroughly in-game
4. Submit a pull request with your changes
5. Include a screenshot showing the translation in action

## Notes

- RimWorld will automatically fall back to English for any missing translations
- You don't need to translate every file - partial translations are supported
- Some technical strings (like mod settings) may require additional translation files

## Language Worker Classes

Common RimWorld language worker classes:
- `LanguageWorker_English`
- `LanguageWorker_Spanish` 
- `LanguageWorker_French`
- `LanguageWorker_German`
- `LanguageWorker_Japanese`
- `LanguageWorker_ChineseSimplified`
- `LanguageWorker_ChineseTraditional`
- `LanguageWorker_Korean`
- `LanguageWorker_Russian`

If your language isn't listed, use `LanguageWorker_English` as a fallback.