# -*- coding: utf-8 -*-
# П94: ранний выход в слое — пробег короче расстояния до ближайшей границы области (цена ×3 на 59.5 кэВ).
import io
p = 'BecquerelMonitor/EfficiencyMaker/ElectronTransport.cs'
s = io.open(p, 'rb').read().decode('utf-8-sig')
old = ("            return d > 0.0 ? d : 0.0;\r\n"
       "        }\r\n"
       "\r\n"
       "        /// <summary>\r\n"
       "        /// Ширина θ₀ многократного рассеяния на шаге <paramref name=\"xOverX0\"/>\r\n")
new = ("            return d > 0.0 ? d : 0.0;\r\n"
       "        }\r\n"
       "\r\n"
       "        /// <summary>\r\n"
       "        /// (`AMBER44`, П94) То же для ЛЮБОЙ области сцены: расстояние от точки\r\n"
       "        /// внутри области до ближайшей её границы, см, — нижняя граница пути до\r\n"
       "        /// выхода из неё по любому направлению. У кольца смотрится и внутренний\r\n"
       "        /// радиус: слой обвязки — кольцо вокруг кристалла, и ближайшая граница\r\n"
       "        /// у него обычно внутренняя. Ранний выход переноса в слое\r\n"
       "        /// (<see cref=\"WalkInLayers\"/>): пробег короче — электрон погибнет в\r\n"
       "        /// этой области при любой траектории, кристалла ему не видать. Без\r\n"
       "        /// этого на 59.5 кэВ каждый фотоэлектрон пробы и оправы шёл десятками\r\n"
       "        /// шагов с разбором луча на каждом (измерено: ×3 к цене истории).\r\n"
       "        /// </summary>\r\n"
       "        static double RegionNearestFace(Region r, double x, double y, double z)\r\n"
       "        {\r\n"
       "            double d = Math.Min(z - r.ZMin, r.ZMax - z);\r\n"
       "            if (r.IsBox)\r\n"
       "            {\r\n"
       "                d = Math.Min(d, Math.Min(r.AX - Math.Abs(x), r.AY - Math.Abs(y)));\r\n"
       "            }\r\n"
       "            else\r\n"
       "            {\r\n"
       "                double rad = Math.Sqrt(x * x + y * y);\r\n"
       "                d = Math.Min(d, r.ROut - rad);\r\n"
       "                if (r.RIn > 0.0)\r\n"
       "                {\r\n"
       "                    d = Math.Min(d, rad - r.RIn);\r\n"
       "                }\r\n"
       "            }\r\n"
       "\r\n"
       "            return d > 0.0 ? d : 0.0;\r\n"
       "        }\r\n"
       "\r\n"
       "        /// <summary>\r\n"
       "        /// Ширина θ₀ многократного рассеяния на шаге <paramref name=\"xOverX0\"/>\r\n")
assert s.count(old) == 1, 'old'
s = s.replace(old, new)
old2 = ("                ElectronData.Material medium = this.CarryMedium(here.Material);\r\n"
        "                double x0 = this.LayerRadiationLength(here.Material);\r\n"
        "                double residual = ElectronData.RangeOf(medium, t);          // г/см²\r\n"
        "                if (!(residual > 0.0))\r\n"
        "                {\r\n"
        "                    return false;\r\n"
        "                }\r\n"
        "\r\n")
new2 = ("                ElectronData.Material medium = this.CarryMedium(here.Material);\r\n"
        "                double x0 = this.LayerRadiationLength(here.Material);\r\n"
        "                double residual = ElectronData.RangeOf(medium, t);          // г/см²\r\n"
        "                if (!(residual > 0.0))\r\n"
        "                {\r\n"
        "                    return false;\r\n"
        "                }\r\n"
        "\r\n"
        "                // Ранний выход, как в кристалле: пробег короче расстояния до\r\n"
        "                // ближайшей границы области — погибнет в ней при любой траектории.\r\n"
        "                if (residual / density <= RegionNearestFace(here, x, y, z))\r\n"
        "                {\r\n"
        "                    return false;\r\n"
        "                }\r\n"
        "\r\n")
assert s.count(old2) == 1, 'old2'
s = s.replace(old2, new2)
io.open(p, 'wb').write(b'\xef\xbb\xbf' + s.encode('utf-8'))
print('ok')
