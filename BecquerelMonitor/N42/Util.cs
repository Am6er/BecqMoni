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

            //EnergyCalibration
            PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)doc.ActiveResultData.EnergySpectrum.EnergyCalibration;
            if (doc.ActiveResultData.BackgroundEnergySpectrum != null)
            {
                rad.EnergyCalibration = new EnergyCalibration[2];

            } else
            {
                rad.EnergyCalibration = new EnergyCalibration[1];
            }
            rad.EnergyCalibration[0] = new EnergyCalibration();
            rad.EnergyCalibration[0].id = "SpectrumCalibration";
            for (int i = 0; i < energyCalibration.Coefficients.Length; i++)
            {
                rad.EnergyCalibration[0].CoefficientValues = rad.EnergyCalibration[0].CoefficientValues + energyCalibration.Coefficients[i].ToString() + " ";

            }

            //RadMeasurement
            if (doc.ActiveResultData.BackgroundEnergySpectrum != null)
            {
                rad.RadMeasurement = new RadMeasurement[2];
            } else
            {
                rad.RadMeasurement = new RadMeasurement[1];
            }
                
            rad.RadMeasurement[0] = new RadMeasurement
            {
                id = "SpectrumMeasurement",
                MeasurementClassCode = "Foreground",
                StartDateTime = doc.ActiveResultData.StartTime.ToString(),
                RealTimeDuration = "PT" + doc.ActiveResultData.EnergySpectrum.MeasurementTime + "S",
                //RadMeasurement -> Spectrum
                Spectrum = new Spectrum[1]
            };
            rad.RadMeasurement[0].Spectrum[0] = new Spectrum
            {
                id = "SpectrumData",
                energyCalibrationReference = rad.EnergyCalibration[0].id,
                LiveTimeDuration = "PT" + doc.ActiveResultData.EnergySpectrum.MeasurementTime + "S"
            };
            rad.RadMeasurement[0].Spectrum[0].ChannelData.SpectrumFromArray(doc.ActiveResultData.EnergySpectrum.Spectrum);

            //RadMeasurement -> GrossCounts
            rad.RadMeasurement[0].GrossCounts = new GrossCounts[1];
            rad.RadMeasurement[0].GrossCounts[0] = new GrossCounts
            {
                id = "GrossForeground",
                TotalCounts = doc.ActiveResultData.EnergySpectrum.TotalPulseCount.ToString()
            };

            // if we have background
            if (doc.ActiveResultData.BackgroundEnergySpectrum != null)
            {
                //EnergyCalibration
                energyCalibration = (PolynomialEnergyCalibration)doc.ActiveResultData.BackgroundEnergySpectrum.EnergyCalibration;
                rad.EnergyCalibration[1] = new EnergyCalibration
                {
                    id = "BackgroundCalibration"
                };
                for (int i = 0; i < energyCalibration.Coefficients.Length; i++)
                {
                    rad.EnergyCalibration[1].CoefficientValues = rad.EnergyCalibration[1].CoefficientValues + energyCalibration.Coefficients[i].ToString() + " ";

                }

                //RadMeasurement
                rad.RadMeasurement[1] = new RadMeasurement
                {
                    id = "BackgroundMeasurement",
                    MeasurementClassCode = "Background",
                    StartDateTime = doc.ActiveResultData.StartTime.ToString(),
                    RealTimeDuration = "PT" + doc.ActiveResultData.BackgroundEnergySpectrum.MeasurementTime + "S"
                };

                //RadMeasurement -> Spectrum
                rad.RadMeasurement[1].Spectrum = new Spectrum[1];
                rad.RadMeasurement[1].Spectrum[0] = new Spectrum
                {
                    id = "BackgroundData",
                    energyCalibrationReference = rad.EnergyCalibration[1].id,
                    LiveTimeDuration = "PT" + doc.ActiveResultData.BackgroundEnergySpectrum.MeasurementTime + "S"
                };
                rad.RadMeasurement[1].Spectrum[0].ChannelData.SpectrumFromArray(doc.ActiveResultData.BackgroundEnergySpectrum.Spectrum);

                //RadMeasurement -> GrossCounts
                rad.RadMeasurement[1].GrossCounts = new GrossCounts[1];
                rad.RadMeasurement[1].GrossCounts[0] = new GrossCounts
                {
                    id = "GrossBackground",
                    TotalCounts = doc.ActiveResultData.BackgroundEnergySpectrum.TotalPulseCount.ToString()
                };

            }

            return rad;
        }
    }
}
