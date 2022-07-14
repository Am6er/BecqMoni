using System;
using System.IO;
using System.Windows.Forms;
using System.Xml;

namespace BecquerelMonitor.N42
{
    public class Util
    {
        public Util()
        {

        }

        public RadInstrumentData ExportToN42(DocEnergySpectrum doc)
        {
            RadInstrumentData rad = new RadInstrumentData();

            //RadInstrumentInformation
            rad.RadInstrumentInformation = new RadInstrumentInformation[1];
            rad.RadInstrumentInformation[0] = new RadInstrumentInformation();
            rad.RadInstrumentInformation[0].RadInstrumentVersion = new RadInstrumentVersion[3];
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0].RadInstrumentComponentName = "Hardware";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0].RadInstrumentComponentVersion = doc.ActiveResultData.DeviceConfig.Name;
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1].RadInstrumentComponentName = "SoftwareName";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1].RadInstrumentComponentVersion = "BecqMoni";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2].RadInstrumentComponentName = "Software";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2].RadInstrumentComponentVersion = GlobalConfigManager.GetInstance().VersionString;

            int SpectrumCount = doc.ResultDataFile.ResultDataList.Count;
            bool bgexist = false;

            if (doc.ActiveResultData.BackgroundEnergySpectrum != null)
            {
                bgexist = true;
                rad.EnergyCalibration = new EnergyCalibration[SpectrumCount+1];
                rad.RadMeasurement = new RadMeasurement[SpectrumCount+1];
            } else
            {
                rad.EnergyCalibration = new EnergyCalibration[SpectrumCount];
                rad.RadMeasurement = new RadMeasurement[SpectrumCount];
            }
            

            for (int i = 0; i < SpectrumCount; i++)
            {
                string calibrationId = "SpectrumCalibration" + i;
                string idMeasurement = "SpectrumMeasurement" + i;
                string measurementClassCode = "Foreground" + i;
                string idSpectrum = "SpectrumData";
                string idGrossCounts = "GrossForeground" + i;
                rad.EnergyCalibration[i] = AddCalibration(doc.ResultDataFile.ResultDataList[i], calibrationId);
                rad.RadMeasurement[i] = AddMeasurement(doc.ResultDataFile.ResultDataList[i], idMeasurement, measurementClassCode, idSpectrum, calibrationId, idGrossCounts);
            }

            // if we have background
            if (bgexist)
            {
                //EnergyCalibration
                PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)doc.ActiveResultData.BackgroundEnergySpectrum.EnergyCalibration;
                rad.EnergyCalibration[SpectrumCount] = new EnergyCalibration
                {
                    id = "BackgroundCalibration"
                };
                for (int i = 0; i < energyCalibration.Coefficients.Length; i++)
                {
                    rad.EnergyCalibration[SpectrumCount].CoefficientValues = rad.EnergyCalibration[SpectrumCount].CoefficientValues + energyCalibration.Coefficients[i].ToString() + " ";
                }
                rad.EnergyCalibration[SpectrumCount].CoefficientValues = rad.EnergyCalibration[SpectrumCount].CoefficientValues.Replace(',', '.');

                //RadMeasurement
                rad.RadMeasurement[SpectrumCount] = new RadMeasurement
                {
                    id = "BackgroundMeasurement",
                    MeasurementClassCode = "Background",
                    StartDateTime = doc.ActiveResultData.StartTime.ToString(),
                    RealTimeDuration = "PT" + doc.ActiveResultData.BackgroundEnergySpectrum.MeasurementTime + "S"
                };

                //RadMeasurement -> Spectrum
                rad.RadMeasurement[SpectrumCount].Spectrum = new Spectrum[1];
                rad.RadMeasurement[SpectrumCount].Spectrum[0] = new Spectrum
                {
                    id = "BackgroundData",
                    energyCalibrationReference = rad.EnergyCalibration[SpectrumCount].id,
                    LiveTimeDuration = "PT" + doc.ActiveResultData.BackgroundEnergySpectrum.MeasurementTime + "S"
                };
                rad.RadMeasurement[SpectrumCount].Spectrum[0].ChannelData.SpectrumFromArray(doc.ActiveResultData.BackgroundEnergySpectrum.Spectrum);

                //RadMeasurement -> GrossCounts
                rad.RadMeasurement[SpectrumCount].GrossCounts = new GrossCounts[1];
                rad.RadMeasurement[SpectrumCount].GrossCounts[0] = new GrossCounts
                {
                    id = "GrossBackground",
                    TotalCounts = doc.ActiveResultData.BackgroundEnergySpectrum.TotalPulseCount.ToString()
                };
            }

            return rad;
        }

        private RadMeasurement AddMeasurement(ResultData data, string idMeasurement, string measurementClassCode, string idSpectrum, string idEnergyCalibration, string idGrossCounts)
        {
            RadMeasurement rad = new RadMeasurement
            {
                id = idMeasurement, //"SpectrumMeasurement",
                MeasurementClassCode = measurementClassCode, // "Foreground",
                StartDateTime = data.StartTime.ToString(),
                RealTimeDuration = "PT" + data.EnergySpectrum.MeasurementTime + "S",
                //RadMeasurement -> Spectrum
                Spectrum = new Spectrum[1]
            };
            rad.Spectrum[0] = new Spectrum
            {
                id = idSpectrum, //"SpectrumData",
                energyCalibrationReference = idEnergyCalibration, //rad.EnergyCalibration[0].id,
                LiveTimeDuration = "PT" + data.EnergySpectrum.MeasurementTime + "S"
            };
            rad.Spectrum[0].ChannelData.SpectrumFromArray(data.EnergySpectrum.Spectrum);

            //RadMeasurement -> GrossCounts
            rad.GrossCounts = new GrossCounts[1];
            rad.GrossCounts[0] = new GrossCounts
            {
                id = idGrossCounts, //"GrossForeground",
                TotalCounts = data.EnergySpectrum.TotalPulseCount.ToString()
            };
            return rad;
        }

        private EnergyCalibration AddCalibration(ResultData input, string id)
        {
            PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)input.EnergySpectrum.EnergyCalibration;
            EnergyCalibration output = new EnergyCalibration();
            output.id = id;
            for (int i = 0; i < energyCalibration.Coefficients.Length; i++)
            {
                output.CoefficientValues = output.CoefficientValues + energyCalibration.Coefficients[i].ToString() + " ";

            }
            output.CoefficientValues = output.CoefficientValues.Replace(',', '.');
            return output;
        }

        public DocEnergySpectrum ImportFromN42(RadInstrumentData rad, DocEnergySpectrum doc, string filename)
        {
            int SpectrumCount = rad.RadMeasurement.Length;

            string SpectrumName = Path.GetFileNameWithoutExtension(filename);
            doc.Filename = SpectrumName + ".xml";
            doc.Text = SpectrumName;

            for (int i = 0; i < SpectrumCount; i++)
            {
                RadMeasurement radMeasurement = rad.RadMeasurement[i];
                EnergyCalibration radCalibration = rad.EnergyCalibration[i];
                ResultData resultData = new ResultData();
                resultData.MeasurementController = doc.ActiveResultData.MeasurementController;
                resultData.EnergySpectrum.TotalPulseCount = 0;
                try
                {
                    resultData.EnergySpectrum.TotalPulseCount = int.Parse(radMeasurement.GrossCounts[0].TotalCounts);
                }
                catch { }

                resultData.EnergySpectrum.ValidPulseCount = resultData.EnergySpectrum.TotalPulseCount;
                resultData.SampleInfo.Time = DateTime.Now;

                try
                {
                    resultData.SampleInfo.Time = XmlConvert.ToDateTime(radMeasurement.StartDateTime); //"10/13/2021 07:14:57"
                }
                catch { }

                resultData.SampleInfo.Name = SpectrumName;
                resultData.SampleInfo.Note = "";

                try
                {
                    foreach (RadInstrumentInformation radIterator in rad.RadInstrumentInformation)
                    {
                        resultData.SampleInfo.Note = resultData.SampleInfo.Note + radIterator.RadInstrumentManufacturerName + " - " + radIterator.RadInstrumentModelName + Environment.NewLine;
                        foreach (RadInstrumentVersion radInsIterator in radIterator.RadInstrumentVersion)
                        {
                            resultData.SampleInfo.Note = resultData.SampleInfo.Note + radInsIterator.RadInstrumentComponentName + " - " + radInsIterator.RadInstrumentComponentVersion + Environment.NewLine;
                        }

                    }

                    foreach (RadDetectorInformation radDetIterator in rad.RadDetectorInformation)
                    {
                        resultData.SampleInfo.Note = resultData.SampleInfo.Note + radDetIterator.RadDetectorCategoryCode + " - " + radDetIterator.RadDetectorKindCode + Environment.NewLine;
                    }
                }
                catch
                { }

                int ElapsedTime = (int)XmlConvert.ToTimeSpan(radMeasurement.Spectrum[0].LiveTimeDuration).TotalSeconds;
                resultData.EnergySpectrum.MeasurementTime = ElapsedTime;
                doc.ActiveResultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(ElapsedTime);
                doc.ActiveResultData.ResultDataStatus.ElapsedTime = TimeSpan.FromSeconds(ElapsedTime);
                doc.ActiveResultData.ResultDataStatus.PresetTime = ElapsedTime;

                string[] n42SpectrimCounts = radMeasurement.Spectrum[0].ChannelData.Value.Replace("\n", string.Empty).Split(new string[] { " " }, StringSplitOptions.None);
                n42SpectrimCounts = Array.FindAll(n42SpectrimCounts, isNotN42SpectrumValid);
                int NumberOfChanels = n42SpectrimCounts.Length;
                resultData.EnergySpectrum.Spectrum = new int[NumberOfChanels];
                for (int k = 0; k < NumberOfChanels; k++)
                {
                    resultData.EnergySpectrum.Spectrum[k] = int.Parse(n42SpectrimCounts[k]);
                }

                try
                {
                    string[] n42CalibrationCoeff = radCalibration.CoefficientValues.Replace("\n", string.Empty).Split(new string[] { " " }, StringSplitOptions.None);
                    n42CalibrationCoeff = Array.FindAll(n42CalibrationCoeff, isNotN42SpectrumValid);
                    int PolynomialOrder = n42CalibrationCoeff.Length - 1;

                    if (PolynomialOrder > 4)
                    {
                        throw new Exception("Unsupported calibration points number. Got polynom order = " + PolynomialOrder);
                    }

                    double[] coefficients = new double[PolynomialOrder + 1];

                    for (int k = 0; k < coefficients.Length; k++)
                    {
                        coefficients[k] = double.Parse(n42CalibrationCoeff[k]);
                    }
                    resultData.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                    PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)resultData.EnergySpectrum.EnergyCalibration;
                    energyCalibration.PolynomialOrder = PolynomialOrder;
                    for (int k = 0; k < coefficients.Length; k++)
                    {
                        energyCalibration.Coefficients[k] = coefficients[k];
                    }

                    if (!energyCalibration.CheckCalibration())
                    {
                        MessageBox.Show("The calibration function should be monotonically increasing at channel > 0. Re-check Calibration points!");
                    }
                }
                catch
                {
                    MessageBox.Show("N42 EnergyBoundaryValues not supported. Using current calibration.");
                }

                doc.ResultDataFile.ResultDataList.Add(resultData);
            }
            doc.ResultDataFile.ResultDataList[0].Visible = true;
            return doc;
        }

        private bool isNotN42SpectrumValid(string str)
        {
            if (str == "" || str == "\n")
            {
                return false;
            }
            else
            {
                return true;
            }
        }

    }
}
