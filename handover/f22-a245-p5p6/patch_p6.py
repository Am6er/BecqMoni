# -*- coding: utf-8 -*-
"""
Доля П6 строки `A244` (полоса F22, 05.09.2026): печать и разбор чисел в
зонах интереса `ROI*` — инвариантной культурой, обе стороны разом.

Устройство и оговорки — те же, что у `patch_p5.py` (два прохода, CRLF,
счёт вхождений). Особое в этой доле:

* ⛔ Пять мест здесь стояли с ЯВНОЙ `CultureInfo.CurrentCulture` — то есть
  кто-то раньше решил печатать число культурой человека. Правило Amber
  05.09.2026 отменяет это решение: разделитель дробной части — ВСЕГДА точка,
  и поле, куда число кладётся, читается `UserNumber`-ом, который первым
  пробует инвариант.
* ⚠ `string.Concat(new object[] { …, double, … })` сканер НЕ ВИДИТ вовсе:
  число превращается в строку внутри `Concat`, культурой потока. Два таких
  места в `GetPrimitiveRegionString` найдены отдельным поиском.
"""
import io, sys, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

EDITS = [
    # ---------------- ROIConfigForm.cs ---------------------------------
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                string text = Resources.NewROIConfigPrefix + "(" + i.ToString() + ").xml";',
     '                string text = Resources.NewROIConfigPrefix + "(" + i.ToString(CultureInfo.InvariantCulture) + ").xml";'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                text = "New ROI(" + this.newROIIndex + ")";',
     '                text = "New ROI(" + this.newROIIndex.ToString(CultureInfo.InvariantCulture) + ")";'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                string text = roidefinitionData.LowerLimit.ToString() + " - " + roidefinitionData.UpperLimit.ToString() + " " + Resources.kev;',
     '                string text = roidefinitionData.LowerLimit.ToString(CultureInfo.InvariantCulture) + " - " + roidefinitionData.UpperLimit.ToString(CultureInfo.InvariantCulture) + " " + Resources.kev;'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                row.Cells.Add(new Cell(roidefinitionData.ROIPrimitives.Count.ToString()));',
     '                row.Cells.Add(new Cell(roidefinitionData.ROIPrimitives.Count.ToString(CultureInfo.InvariantCulture)));'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                row.Cells[1].Text = roidefinitionData.LowerLimit.ToString() + " - " + roidefinitionData.UpperLimit.ToString() + " " + Resources.kev;',
     '                row.Cells[1].Text = roidefinitionData.LowerLimit.ToString(CultureInfo.InvariantCulture) + " - " + roidefinitionData.UpperLimit.ToString(CultureInfo.InvariantCulture) + " " + Resources.kev;'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                row.Cells[2].Text = roidefinitionData.ROIPrimitives.Count.ToString();',
     '                row.Cells[2].Text = roidefinitionData.ROIPrimitives.Count.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '            this.doubleTextBox3.Text = roi.BecquerelCoefficient.ToString();\n'
     '            this.doubleTextBox4.Text = roi.BecquerelCoefficientError.ToString();\n'
     '            this.doubleTextBox5.Text = roi.PeakEnergy.ToString();\n'
     '            this.doubleTextBox6.Text = roi.HalfLife.ToString();\n'
     '            this.doubleTextBox7.Text = roi.Intencity.ToString();\n'
     '            this.doubleTextBox1.Text = roi.LowerLimit.ToString();\n'
     '            this.doubleTextBox2.Text = roi.UpperLimit.ToString();',
     '            this.doubleTextBox3.Text = roi.BecquerelCoefficient.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox4.Text = roi.BecquerelCoefficientError.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox5.Text = roi.PeakEnergy.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox6.Text = roi.HalfLife.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox7.Text = roi.Intencity.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox1.Text = roi.LowerLimit.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox2.Text = roi.UpperLimit.ToString(CultureInfo.InvariantCulture);'),
    # Пять мест с ЯВНОЙ культурой человека — правило Amber 05.09.2026 их
    # отменяет: число печатается инвариантом, а поле читается `UserNumber`.
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                CultureInfo.CurrentCulture, Resources.BqCoeffFromCurve, k.Value, k.Error));',
     '                CultureInfo.InvariantCulture, Resources.BqCoeffFromCurve, k.Value, k.Error));'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                this.doubleTextBox3.Text = k.Value.ToString(CultureInfo.CurrentCulture);\n'
     '                this.doubleTextBox4.Text = k.Error.ToString(CultureInfo.CurrentCulture);',
     '                this.doubleTextBox3.Text = k.Value.ToString(CultureInfo.InvariantCulture);\n'
     '                this.doubleTextBox4.Text = k.Error.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                    this.doubleTextBox3.Text = this.activeROIDefinition.BecquerelCoefficient\n'
     '                        .ToString(CultureInfo.CurrentCulture);\n'
     '                    this.doubleTextBox4.Text = this.activeROIDefinition.BecquerelCoefficientError\n'
     '                        .ToString(CultureInfo.CurrentCulture);',
     '                    this.doubleTextBox3.Text = this.activeROIDefinition.BecquerelCoefficient\n'
     '                        .ToString(CultureInfo.InvariantCulture);\n'
     '                    this.doubleTextBox4.Text = this.activeROIDefinition.BecquerelCoefficientError\n'
     '                        .ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                string text = (num + 1).ToString() + ") " + primitive.Translation;',
     '                string text = (num + 1).ToString(CultureInfo.InvariantCulture) + ") " + primitive.Translation;'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                row.Cells.Add(new Cell((num + 1).ToString()));',
     '                row.Cells.Add(new Cell((num + 1).ToString(CultureInfo.InvariantCulture)));'),
    # ⚠ `string.Concat(object[])` — число становится строкой ВНУТРИ Concat,
    #    культурой потока; сканер такого места не видит вовсе.
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                result = string.Concat(new object[]\n'
     '                {\n'
     '                    roisimpleDifferenceData.LowerLimit,\n'
     '                    " - ",\n'
     '                    roisimpleDifferenceData.UpperLimit,\n'
     '                    " " + Resources.kev\n'
     '                });',
     '                result = string.Concat(new object[]\n'
     '                {\n'
     '                    roisimpleDifferenceData.LowerLimit.ToString(CultureInfo.InvariantCulture),\n'
     '                    " - ",\n'
     '                    roisimpleDifferenceData.UpperLimit.ToString(CultureInfo.InvariantCulture),\n'
     '                    " " + Resources.kev\n'
     '                });'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                result = string.Concat(new object[]\n'
     '                {\n'
     '                    roicovellMethodData.LowerLimit,\n'
     '                    " - ",\n'
     '                    roicovellMethodData.UpperLimit,\n'
     '                    " " + Resources.kev\n'
     '                });',
     '                result = string.Concat(new object[]\n'
     '                {\n'
     '                    roicovellMethodData.LowerLimit.ToString(CultureInfo.InvariantCulture),\n'
     '                    " - ",\n'
     '                    roicovellMethodData.UpperLimit.ToString(CultureInfo.InvariantCulture),\n'
     '                    " " + Resources.kev\n'
     '                });'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                    becquerelCoefficient = double.Parse(this.doubleTextBox3.Text);\n'
     '                    becquerelCoefficientError = double.Parse(this.doubleTextBox4.Text);\n'
     '                }\n'
     '                peakEnergy = double.Parse(this.doubleTextBox5.Text);\n'
     '                halfLife = double.Parse(this.doubleTextBox6.Text);\n'
     '                intencity = double.Parse(this.doubleTextBox7.Text);\n'
     '                lowerLimit = double.Parse(this.doubleTextBox1.Text);\n'
     '                upperLimit = double.Parse(this.doubleTextBox2.Text);',
     '                    becquerelCoefficient = UserNumber.ParseDouble(this.doubleTextBox3.Text);\n'
     '                    becquerelCoefficientError = UserNumber.ParseDouble(this.doubleTextBox4.Text);\n'
     '                }\n'
     '                peakEnergy = UserNumber.ParseDouble(this.doubleTextBox5.Text);\n'
     '                halfLife = UserNumber.ParseDouble(this.doubleTextBox6.Text);\n'
     '                intencity = UserNumber.ParseDouble(this.doubleTextBox7.Text);\n'
     '                lowerLimit = UserNumber.ParseDouble(this.doubleTextBox1.Text);\n'
     '                upperLimit = UserNumber.ParseDouble(this.doubleTextBox2.Text);'),
    ('BecquerelMonitor/ROIConfigForm.cs', 1,
     '                this.doubleTextBox2.Text = upperLimit.ToString();',
     '                this.doubleTextBox2.Text = upperLimit.ToString(CultureInfo.InvariantCulture);'),

    # ---------------- ROIConfigManager.cs ------------------------------
    ('BecquerelMonitor/ROIConfigManager.cs', 1,
     'using System.Drawing;\nusing System.IO;',
     'using System.Drawing;\nusing System.Globalization;\nusing System.IO;'),
    ('BecquerelMonitor/ROIConfigManager.cs', 1,
     '            System.Diagnostics.Trace.WriteLine(string.Format(\n'
     '                "ROI config \\"{0}\\": элемент <{1}> (строка {2}) программе неизвестен, "',
     '            System.Diagnostics.Trace.WriteLine(string.Format(\n'
     '                CultureInfo.InvariantCulture,\n'
     '                "ROI config \\"{0}\\": элемент <{1}> (строка {2}) программе неизвестен, "'),

    # ---------------- ROICovellMethodControl.cs ------------------------
    ('BecquerelMonitor/ROICovellMethodControl.cs', 1,
     'using System;\n',
     'using System;\nusing System.Globalization;\n'),
    ('BecquerelMonitor/ROICovellMethodControl.cs', 1,
     '            this.doubleTextBox3.Text = roicovellMethodData.Coefficient.ToString();\n'
     '            this.doubleTextBox4.Text = roicovellMethodData.CoefficientError.ToString();\n'
     '            this.doubleTextBox1.Text = roicovellMethodData.LowerLimit.ToString();\n'
     '            this.doubleTextBox2.Text = roicovellMethodData.UpperLimit.ToString();\n'
     '            this.doubleTextBox5.Text = roicovellMethodData.LeftRegionCenter.ToString();\n'
     '            this.doubleTextBox6.Text = roicovellMethodData.RightRegionCenter.ToString();\n'
     '            this.doubleTextBox7.Text = roicovellMethodData.LeftRegionWidth.ToString();\n'
     '            this.doubleTextBox8.Text = roicovellMethodData.RightRegionWidth.ToString();',
     '            this.doubleTextBox3.Text = roicovellMethodData.Coefficient.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox4.Text = roicovellMethodData.CoefficientError.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox1.Text = roicovellMethodData.LowerLimit.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox2.Text = roicovellMethodData.UpperLimit.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox5.Text = roicovellMethodData.LeftRegionCenter.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox6.Text = roicovellMethodData.RightRegionCenter.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox7.Text = roicovellMethodData.LeftRegionWidth.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox8.Text = roicovellMethodData.RightRegionWidth.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/ROICovellMethodControl.cs', 1,
     '                coefficient = double.Parse(this.doubleTextBox3.Text);\n'
     '                coefficientError = double.Parse(this.doubleTextBox4.Text);\n'
     '                lowerLimit = double.Parse(this.doubleTextBox1.Text);\n'
     '                upperLimit = double.Parse(this.doubleTextBox2.Text);\n'
     '                leftRegionCenter = double.Parse(this.doubleTextBox5.Text);\n'
     '                rightRegionCenter = double.Parse(this.doubleTextBox6.Text);\n'
     '                leftRegionWidth = double.Parse(this.doubleTextBox7.Text);\n'
     '                rightRegionWidth = double.Parse(this.doubleTextBox8.Text);',
     '                coefficient = UserNumber.ParseDouble(this.doubleTextBox3.Text);\n'
     '                coefficientError = UserNumber.ParseDouble(this.doubleTextBox4.Text);\n'
     '                lowerLimit = UserNumber.ParseDouble(this.doubleTextBox1.Text);\n'
     '                upperLimit = UserNumber.ParseDouble(this.doubleTextBox2.Text);\n'
     '                leftRegionCenter = UserNumber.ParseDouble(this.doubleTextBox5.Text);\n'
     '                rightRegionCenter = UserNumber.ParseDouble(this.doubleTextBox6.Text);\n'
     '                leftRegionWidth = UserNumber.ParseDouble(this.doubleTextBox7.Text);\n'
     '                rightRegionWidth = UserNumber.ParseDouble(this.doubleTextBox8.Text);'),
    ('BecquerelMonitor/ROICovellMethodControl.cs', 1,
     '                this.doubleTextBox2.Text = upperLimit.ToString();',
     '                this.doubleTextBox2.Text = upperLimit.ToString(CultureInfo.InvariantCulture);'),

    # ---------------- ROISimpleDifferenceControl.cs --------------------
    ('BecquerelMonitor/ROISimpleDifferenceControl.cs', 1,
     'using System;\n',
     'using System;\nusing System.Globalization;\n'),
    ('BecquerelMonitor/ROISimpleDifferenceControl.cs', 1,
     '            this.doubleTextBox3.Text = roisimpleDifferenceData.Coefficient.ToString();\n'
     '            this.doubleTextBox4.Text = roisimpleDifferenceData.CoefficientError.ToString();\n'
     '            this.doubleTextBox1.Text = roisimpleDifferenceData.LowerLimit.ToString();\n'
     '            this.doubleTextBox2.Text = roisimpleDifferenceData.UpperLimit.ToString();',
     '            this.doubleTextBox3.Text = roisimpleDifferenceData.Coefficient.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox4.Text = roisimpleDifferenceData.CoefficientError.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox1.Text = roisimpleDifferenceData.LowerLimit.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox2.Text = roisimpleDifferenceData.UpperLimit.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/ROISimpleDifferenceControl.cs', 1,
     '                coefficient = double.Parse(this.doubleTextBox3.Text);\n'
     '                coefficientError = double.Parse(this.doubleTextBox4.Text);\n'
     '                lowerLimit = double.Parse(this.doubleTextBox1.Text);\n'
     '                upperLimit = double.Parse(this.doubleTextBox2.Text);',
     '                coefficient = UserNumber.ParseDouble(this.doubleTextBox3.Text);\n'
     '                coefficientError = UserNumber.ParseDouble(this.doubleTextBox4.Text);\n'
     '                lowerLimit = UserNumber.ParseDouble(this.doubleTextBox1.Text);\n'
     '                upperLimit = UserNumber.ParseDouble(this.doubleTextBox2.Text);'),
    ('BecquerelMonitor/ROISimpleDifferenceControl.cs', 1,
     '            this.doubleTextBox2.Text = upperLimit.ToString();',
     '            this.doubleTextBox2.Text = upperLimit.ToString(CultureInfo.InvariantCulture);'),

    # ---------------- ROIReferenceControl.cs ---------------------------
    ('BecquerelMonitor/ROIReferenceControl.cs', 1,
     'using System;\n',
     'using System;\nusing System.Globalization;\n'),
    ('BecquerelMonitor/ROIReferenceControl.cs', 1,
     '            this.doubleTextBox3.Text = roireferenceData.Coefficient.ToString();\n'
     '            this.doubleTextBox4.Text = roireferenceData.CoefficientError.ToString();',
     '            this.doubleTextBox3.Text = roireferenceData.Coefficient.ToString(CultureInfo.InvariantCulture);\n'
     '            this.doubleTextBox4.Text = roireferenceData.CoefficientError.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/ROIReferenceControl.cs', 1,
     '                coefficient = double.Parse(this.doubleTextBox3.Text);\n'
     '                coefficientError = double.Parse(this.doubleTextBox4.Text);',
     '                coefficient = UserNumber.ParseDouble(this.doubleTextBox3.Text);\n'
     '                coefficientError = UserNumber.ParseDouble(this.doubleTextBox4.Text);'),
]


def main():
    if len(sys.argv) > 1 and sys.argv[1] == '--revert':
        pairs = [(f, n, b, a) for (f, n, a, b) in EDITS]
        pairs.reverse()
    else:
        pairs = EDITS

    # ⛔ Метка порядка байтов (BOM) и перевод строки — СВОИ у каждого файла, и
    #    считать их надо БАЙТАМИ. В этой доле: `ROIConfigForm.cs` — BOM есть, а
    #    строки кончаются ОДНИМ LF (единственный такой файл в обеих долях); три
    #    `ROI*Control.cs` — BOM НЕТ вовсе. Чтение `utf-8-sig` + запись `utf-8-sig`
    #    добавила бы им BOM, и правка на одну строку вышла бы правкой всего файла.
    files = {}
    boms = {}
    for path, times, old, new in pairs:
        full = os.path.join(ROOT, path)
        if full not in files:
            raw = io.open(full, 'rb').read()
            boms[full] = raw[:3] == b'\xef\xbb\xbf'
            files[full] = raw.decode('utf-8-sig')
        src = files[full]
        if '\r\n' in src:
            old = old.replace('\n', '\r\n')
            new = new.replace('\n', '\r\n')
        got = src.count(old)
        if got != times:
            raise SystemExit('ОТКАЗ (ничего не записано): %s — образец встречается %d раз, ждали %d:\n%s'
                             % (path, got, times, old[:160]))
        files[full] = src.replace(old, new)
        print('%-40s x%d  %s' % (os.path.basename(path), times, old.strip().splitlines()[0][:70]))

    total = sum(t for (_, t, _, _) in pairs)
    for full, text in files.items():
        data = text.encode('utf-8')
        if boms[full]:
            data = b'\xef\xbb\xbf' + data
        io.open(full, 'wb').write(data)
    print('файлов переписано: %d, образцов заменено: %d' % (len(files), total))


main()
