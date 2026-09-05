# -*- coding: utf-8 -*-
"""F28, полоса 1 правок: мелкие файлы доли П8."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fix import patch, INV

R = 'BecquerelMonitor/'
JOBS = [
    (R + 'AudioInputDeviceController.cs', [
        ('"BecqMoni: the audio input device could not be opened (id " + deviceId + ", "',
         '"BecqMoni: the audio input device could not be opened (id " + deviceId.ToString(%s) + ", "' % INV, 1),
    ]),
    (R + 'EnergySpectrum.cs', [
        ('+ " (EnergyCalibration == null, каналов " + this.numberOfChannels',
         '+ " (EnergyCalibration == null, каналов " + this.numberOfChannels.ToString(%s)' % INV, 1),
    ]),
    (R + 'GlobalConfigManager.cs', [
        ('string mark = n > 1 ? "(" + (i + 1) + "/" + n + ") " : "";',
         'string mark = n > 1 ? "(" + (i + 1).ToString(%s) + "/" + n.ToString(%s) + ") " : "";' % (INV, INV), 1),
    ]),
    (R + 'ChanNumberChangeDialog.cs', [
        ('if (int.TryParse(textBox1.Text, out int newChan))',
         'if (UserNumber.TryParseInt(textBox1.Text, out int newChan))', 1),
    ], False),
    (R + 'PulseView.cs', [
        ('graphics.DrawString(num4.ToString("f0"),',
         'graphics.DrawString(num4.ToString("f0", %s),' % INV, 1),
        ('graphics.DrawString(this.pulseHeight.ToString("f2"),',
         'graphics.DrawString(this.pulseHeight.ToString("f2", %s),' % INV, 1),
        ('graphics.DrawString(this.maxpulseHeight.ToString("f2"),',
         'graphics.DrawString(this.maxpulseHeight.ToString("f2", %s),' % INV, 1),
    ]),
    (R + 'SpectrumCutOffDialog.cs', [
        ('if (double.TryParse(energytextBox.Text, out double energyVal))',
         'if (UserNumber.TryParseDouble(energytextBox.Text, out double energyVal))', 1),
        ('if (int.TryParse(channeltextBox.Text, out int channel))',
         'if (UserNumber.TryParseInt(channeltextBox.Text, out int channel))', 1),
    ], False),
    (R + 'NuclideSetForm.cs', [
        ('new Cell(nuclideDefinition.Energy.ToString(), nuclideDefinition.Energy)',
         'new Cell(nuclideDefinition.Energy.ToString(%s), nuclideDefinition.Energy)' % INV, 1),
        ('Name = $"New set {this.tableModelSets.Rows.Count + 1}"',
         'Name = "New set " + (this.tableModelSets.Rows.Count + 1).ToString(%s)' % INV, 1),
    ]),
    (R + 'NuclideDefinition.cs', [
        ('return $"{this.name} - {this.energy}";',
         'return this.name + " - " + this.energy.ToString(%s);' % INV, 1),
    ]),
    (R + 'ObsidianDeviceController.cs', [
        ('Trace.WriteLine($"Obsidian: histogram size {e.Hystogram.Length} > document channels {pulseDetector.EnergySpectrum.Spectrum.Length}, dropping update");',
         'Trace.WriteLine(string.Format(%s, "Obsidian: histogram size {0} > document channels {1}, dropping update", e.Hystogram.Length, pulseDetector.EnergySpectrum.Spectrum.Length));' % INV, 1),
    ]),
    (R + 'RadiaCodeDeviceController.cs', [
        ('Trace.WriteLine($"RadiaCode: histogram size {e.Hystogram.Length} > document channels {this.pulseDetector.EnergySpectrum.Spectrum.Length}, dropping update");',
         'Trace.WriteLine(string.Format(%s, "RadiaCode: histogram size {0} > document channels {1}, dropping update", e.Hystogram.Length, this.pulseDetector.EnergySpectrum.Spectrum.Length));' % INV, 1),
    ]),
    (R + 'SqrtFwhmCalibration.cs', [
        ('return String.Format(formula, "c", "b", "a");',
         'return String.Format(%s, formula, "c", "b", "a");' % INV, 1),
        ('return String.Format(formula, coefficients[0], coefficients[1], coefficients[2]);',
         'return String.Format(%s, formula, coefficients[0], coefficients[1], coefficients[2]);' % INV, 1),
    ]),
    (R + 'SimpleSqrtFwhmCalibration.cs', [
        ('return String.Format(formula, "b", "k");',
         'return String.Format(%s, formula, "b", "k");' % INV, 1),
        ('return String.Format(formula, coefficients[0], coefficients[1]);',
         'return String.Format(%s, formula, coefficients[0], coefficients[1]);' % INV, 1),
    ]),
    (R + 'PowerFwhmCalibration.cs', [
        ('return String.Format(formula, "a", "p");',
         'return String.Format(%s, formula, "a", "p");' % INV, 1),
        ('return String.Format(formula, coefficients[0], coefficients[1]);',
         'return String.Format(%s, formula, coefficients[0], coefficients[1]);' % INV, 1),
    ]),
    (R + 'GeometryEditorPanel.cs', [
        ('string.Format(Resources.EfficiencyMakerGeometryUnknownElement, missingZ)',
         'string.Format(%s, Resources.EfficiencyMakerGeometryUnknownElement, missingZ)' % INV, 1),
        ('CultureInfo.CurrentCulture', INV, 3),
    ]),
    (R + 'GeometryMaterialEditorForm.cs', [
        ('CultureInfo.CurrentCulture', INV, 8),
    ]),
]

grand = 0
for job in JOBS:
    path, pairs = job[0], job[1]
    need = job[2] if len(job) > 2 else True
    n = patch(path, pairs, need)
    grand += n
    print('%-55s %d' % (path, n))
print('ИТОГО мест:', grand)
