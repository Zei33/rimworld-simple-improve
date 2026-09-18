# Languages

Nine languages ship with this mod: English, ChineseSimplified, French, German, Japanese, Polish,
PortugueseBrazilian, Russian and Spanish.

This file is a note for contributors. It is not shipped: `build.sh` stages only the XML under this
folder, so subscribers get the translations and not this.

## What each language folder holds

```
<Language>/
├── LanguageInfo.xml
├── Keyed/
│   └── SimpleImprove_Keys.xml
└── DefInjected/
    ├── JobDef/
    │   └── Jobs_Improve.xml
    ├── WorkGiverDef/
    │   └── WorkGivers_Improve.xml
    └── WorkTypeDef/
        └── WorkTypes_Improve.xml
```

There is no `About/About.xml`. RimWorld supports translating a mod's name and description that way,
and this mod does not do it, so the entry in the in-game mod list is English in every language.
That is a real gap rather than an oversight in this file, which used to claim the opposite.

## The rule that matters

**A key added to one language is a shipped bug, not a warning.** The player sees a raw key name in
the UI and nothing in the game fails loudly enough to catch it. Every key has to land in all nine
folders in the same commit.

`Tests/LanguageParityTests.cs` enforces that in both directions: every language must declare the
same Keyed set, the same DefInjected def types and the same injected fields, and every key the C#
asks for must exist. It also fails a language whose injection file is byte-identical to English,
which is what a copied and untranslated file looks like.

That fixture exists because this mod shipped a whole family of the defect and nobody noticed: there
was no `DefInjected/WorkGiverDef` in any language, so the improve entry in the float menu and the
work tab rendered part-English everywhere, while all 63 Keyed strings were present in all nine. A
player reported it as a missing Chinese translation on 28 October 2025 and the obvious check said
the translations were complete.

## Translating

- Keep the key names and the XML structure exactly. Translate only the text between the tags.
- Keep `{0}`, `{1}` and `{2}` as they are, including their order. Several strings quote numbers
  whose meaning depends on position.
- `WorkGiverDef.gerund` and `WorkGiverDef.verb` are not gerunds and verbs in most languages. They
  are substituted into vanilla format strings that reorder their arguments per language, so copy
  the shape of that language's own vanilla entries rather than translating the English word.
  Japanese puts the target first, so its gerunds open with the particle を; German uses a noun
  phrase; Russian uses an infinitive.
- Check the string the player actually sees before naming a UI element. The Work tab column is
  headed with the work type's `labelShort`, and the Work tab itself is called 優先順位 in Japanese
  rather than anything meaning "work".

## Language worker classes

`LanguageInfo.xml` names one. The shipped classes are `LanguageWorker_English`,
`LanguageWorker_Spanish`, `LanguageWorker_French`, `LanguageWorker_German`,
`LanguageWorker_Japanese`, `LanguageWorker_ChineseSimplified`, `LanguageWorker_ChineseTraditional`,
`LanguageWorker_Korean`, `LanguageWorker_Russian` and `LanguageWorker_Polish`. A language with no
specific worker uses `LanguageWorker_Default`.
