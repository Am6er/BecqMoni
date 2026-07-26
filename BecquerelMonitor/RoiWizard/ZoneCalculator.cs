using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.RoiWizard
{
    public enum RoiStyle
    {
        // маркеры: границы −10, высота задаётся Intencity
        Markers,
        // зоны вокруг пика
        Zones,
        ZonesWithMarkers
    }

    public enum ZoneWidthMode
    {
        // процент от энергии — как задаётся ширина ROI в самом BecqMoni
        PercentOfEnergy,
        // k × FWHM по модели разрешения
        FwhmFactor
    }

    // Границы ROI-зоны. Вынесено из SetExporter, чтобы проверки и тесты не тянули
    // за собой типы BecqMoni: расчёт границ — чистая арифметика над энергией.
    public class ZoneCalculator
    {
        readonly ResolutionModel resolution;

        public ZoneCalculator(ResolutionModel resolution)
        {
            this.resolution = resolution;
            this.Style = RoiStyle.Markers;
            this.WidthMode = ZoneWidthMode.PercentOfEnergy;
            this.ZonePercent = 5.0;
            this.ZoneFwhmFactor = 3.0;
        }

        public RoiStyle Style { get; set; }
        public ZoneWidthMode WidthMode { get; set; }
        public double ZonePercent { get; set; }
        public double ZoneFwhmFactor { get; set; }

        // Для режима маркеров BecqMoni ожидает -10: это признак того, что зоны нет,
        // а запись рисуется штрихом высотой по Intencity.
        public void LimitsFor(SpectralLine line, out double lower, out double upper)
        {
            if (this.Style == RoiStyle.Markers)
            {
                lower = -10;
                upper = -10;
                return;
            }
            double halfWidth = this.WidthMode == ZoneWidthMode.PercentOfEnergy
                ? line.Energy * this.ZonePercent / 100.0 / 2.0
                : this.ZoneFwhmFactor * this.resolution.Fwhm(line.Energy) / 2.0;
            lower = Math.Floor(line.Energy - halfWidth);
            upper = Math.Ceiling(line.Energy + halfWidth);
        }
    }
}
