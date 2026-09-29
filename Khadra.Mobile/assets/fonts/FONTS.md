# Bundled fonts and their licences

The customer app is set in these fonts (`pubspec.yaml`, `fonts:`). Both licence texts are bundled with the app as assets and registered with Flutter's licence registry at start (`lib/core/fonts/font_licences.dart`), so they travel inside every build.

Both families are licensed under the SIL Open Font License, Version 1.1. The licence texts beside this file
are copied unchanged from each family's official repository, fetched on 2026-09-29:

| Licence file | Family | Copied from |
|---|---|---|
| `OFL-Manrope.txt` | Manrope | https://github.com/googlefonts/manrope/blob/master/OFL.txt (the same text Google Fonts ships) |
| `OFL-NotoKufiArabic.txt` | Noto Kufi Arabic | https://github.com/notofonts/arabic/blob/main/OFL.txt (the same text Google Fonts ships) |

Each font file carries its own copyright notice and names its licence in its metadata. The licence requires
the notice to travel with the font too, and the files' notices are not word for word the ones heading the
licence texts above (the year, and the repository or holder named), so each is reproduced here exactly as the
file carries it:

| File | Family and style | Version | Copyright notice in the file | Licence named in the file | SHA-256 |
|---|---|---|---|---|---|
| `Manrope-400.ttf` | Manrope Regular | 4.504 | Copyright 2019 The Manrope Project Authors (https://github.com/sharanda/manrope) | http://scripts.sil.org/OFL | `a13d9b41b0a471ce58f0e46d377fa3cc76615e4632c3b15eb397caadf7a13f0a` |
| `Manrope-500.ttf` | Manrope Medium | 4.504 | Copyright 2019 The Manrope Project Authors (https://github.com/sharanda/manrope) | http://scripts.sil.org/OFL | `f433e5d333be1128d8ec8a28b8aadd0314d10e14e4105406076666bc830fc15d` |
| `Manrope-600.ttf` | Manrope SemiBold | 4.504 | Copyright 2019 The Manrope Project Authors (https://github.com/sharanda/manrope) | http://scripts.sil.org/OFL | `6bad1a774228464cc88b8b0271555b266f7a3c64a7bedf15e165fdab4f6ac0ce` |
| `Manrope-700.ttf` | Manrope Bold | 4.504 | Copyright 2019 The Manrope Project Authors (https://github.com/sharanda/manrope) | http://scripts.sil.org/OFL | `b9584e099e0e7f1e1e914c3037aabb9010c50346a3ce8df5eeeeecc0563044d8` |
| `Manrope-800.ttf` | Manrope ExtraBold | 4.504 | Copyright 2019 The Manrope Project Authors (https://github.com/sharanda/manrope) | http://scripts.sil.org/OFL | `b8f802f05b322a7345805f5b89f8bb976604cc719a91530918d5ce3c47160950` |
| `NotoKufiArabic-400.ttf` | Noto Kufi Arabic Regular | 2.109 | Copyright 2019-2022 Google LLC. All Rights Reserved. | https://openfontlicense.org | `40445bc7084e6fb2f402d45646174493883bb944d2e70132e578249e24e39e2e` |
| `NotoKufiArabic-500.ttf` | Noto Kufi Arabic Medium | 2.109 | Copyright 2019-2022 Google LLC. All Rights Reserved. | https://openfontlicense.org | `359ea9af3fdf440d29e0432ccf6883e2572c17ee46147211d096bf73da968b1e` |
| `NotoKufiArabic-600.ttf` | Noto Kufi Arabic SemiBold | 2.109 | Copyright 2019-2022 Google LLC. All Rights Reserved. | https://openfontlicense.org | `14c29af737bc90bafdaa6c043868f5fc6c186f0b5e3482e93cf87ea6094eb0a1` |
| `NotoKufiArabic-700.ttf` | Noto Kufi Arabic Bold | 2.109 | Copyright 2019-2022 Google LLC. All Rights Reserved. | https://openfontlicense.org | `24b17f81d261dbc0192e3fbd000c6e51b4f36aedfbc3d8598a91992b842f861d` |

When a font file is replaced, update its row — its version, notice and hash — and its licence file if the family's
licence text changed.
