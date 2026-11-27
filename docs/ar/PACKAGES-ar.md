# الحزم: تثبيت ControlPhone عبر مدير حزم

يصدر ControlPhone ثلاثة ملفات في كل إصدار. اثنان للمستخدم، وواحد لمديري الحزم:

| الملف | لمن | الحجم |
|---|---|---|
| `ControlPhone-Setup-v<version>-win64.exe` | تثبيت عادي (Inno Setup، لكل مستخدم، بدون صلاحيات مسؤول) | ~58 MB |
| `ControlPhone-v<version>-win64.zip` | نسخة محمولة — فُكّ الضغط في أي مكان وشغّل `ControlPhone.exe` | ~80 MB |
| `ControlPhone-v<version>-manifests.zip` | ملفات winget وScoop، مولَّدة من الملفين أعلاه | ~2 KB |

## Scoop

```powershell
scoop bucket add controlphone https://github.com/write2mustaf-cpu/scoop-controlphone
scoop install controlphone
scoop update controlphone      # لاحقاً
```

الملف يثبّت النسخة المحمولة، ويضع `ControlPhone.exe` في `PATH` باسم `controlphone`، ويضيف اختصاراً في قائمة ابدأ،
ويحفظ مجلد **`data\`** — وهو مكان `config.json` وسجلات الأجهزة وسجل المحادثات ومفاتيح API — فلا يضيع إعدادك عند
`scoop update`.

## winget

الإصدار يُرفق ملفات winget الثلاثة، وهي جاهزة للتقديم ولم تُقبل بعد في الفهرس العام:

```powershell
# بعد قبول الملفات في microsoft/winget-pkgs:
winget install Write2Mustaf.ControlPhone
```

التقديم طلب دمج إلى `microsoft/winget-pkgs`:

1. فُكّ `ControlPhone-v<version>-manifests.zip`
2. انسخ ملفات YAML الثلاثة إلى `manifests/w/Write2Mustaf/ControlPhone/<version>/`
3. افتح طلب الدمج (يشغّل فريقهم `winget validate` واختبار تثبيت)

الملفات من نوع `InstallerType: inno` و`Scope: user` مع مفاتيح التثبيت الصامت التي يدعمها المثبّت فعلاً
(`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`)، لذا يعمل `winget install` و`winget upgrade`.

## لماذا تُولَّد الملفات ولا تُكتب يدوياً

الملف يحمل رقم الإصدار وSHA256 للملف الذي سيُنزّله المستخدم بالضبط. وعند الكتابة اليدوية ينحرف أحدهما، فيفشل التثبيت
بخطأ عدم تطابق البصمة. لذلك يقوم `tools/package_manifests.py` بما يلي:

- يقرأ ملفي الإصدار ويحسب SHA256 بنفسه، ويرفض العمل إذا كان أحدهما مفقوداً أو فارغاً؛
- يرفض نسخة محلية مُعاد تسميتها — يجب أن يكون الرابط اسم ملف الإصدار الحقيقي، وإلا أشار الملف إلى رابط ميت؛
- يكتب رقم الإصدار في كل ملف ثم **يقرأ ما كتبه** للتحقق من الإصدار والبصمة ونوع المثبّت، ومن أن `persist` في Scoop
  ما زال يغطي `data\`؛
- يُخرج `manifests.json` (الاسم، الحجم بالبايت، البصمة، الرابط) لتقارنه صفحة الإصدار أو أي أتمتة لاحقة.

يشغّله CI بعد بناء المثبّت مباشرة، فتكون الملفات المرفقة وصفاً دقيقاً للملفات المرفقة بجوارها:

```powershell
python tools/package_manifests.py --version 3.15.0 `
  --setup dist/ControlPhone-Setup-v3.15.0-win64.exe `
  --zip   dist/ControlPhone-v3.15.0-win64.zip `
  --out   dist/manifests
```

## نشر إصدار جديد

1. ادفع الوسم — يبني CI، ويجري الفحص الذاتي، ويحزم، وينشر الإصدار بالملفات الثلاثة؛
2. شغّل الأمر أعلاه على الملفات المنشورة (أو أعد استخدام `-manifests.zip` المرفق)؛
3. ثبّت `bucket/controlphone.json` في حاوية Scoop — ويتولى `checkver`/`autoupdate` الإصدار التالي.

## ما يُتحقق منه آلياً

- `ControlPhone.exe --selfcheck` — فحوص التطبيق الـ275، على البناء وعلى النسخة المثبَّتة؛
- المثبّت يُثبَّت صامتاً، ويُفحص وجود `ControlPhone.exe` و`lang\ar.js` و`scrcpy\adb.exe` و`scrcpy\scrcpy-server`
  و`docs\AI-ASSISTANT.md`، ثم يُلغى تثبيته ويُتأكد أنه لم يترك شيئاً؛
- مولّد الملفات يعيد التحقق من كل ملف يكتبه.

ما **لا** يُتحقق منه: `winget validate` و`scoop install` لا يعملان في CI هذا (لا winget ولا Scoop على الخادم)، لذا
أول تثبيت حقيقي عبر مدير حزم يقع على عاتقك. الملفات بيانات بسيطة — وأي حقل خاطئ يفشل بوضوح عند التثبيت، لا بصمت.
