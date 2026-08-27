using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor
{
    public class Package
    {
        public Package()
        {
            this.isStandAlone = CheckStandAlone();
        }

        public static Package GetInstance()
        {
            return Package.instance;

        }

        bool CheckStandAlone()
        {
            try
            {
                ApplicationDeployment clickOnceCheck = ApplicationDeployment.CurrentDeployment;
            }
            catch
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Корень ПОРТАТИВНОЙ раскладки (<see cref="IsStandAlone"/>) — КАТАЛОГ
        /// СБОРКИ, а не текущий каталог процесса.
        ///
        /// ⛔ Прежде портативные ветки отдавали голую строку <c>config\…</c>,
        /// то есть путь ОТНОСИТЕЛЬНЫЙ, и открывался он от рабочего каталога
        /// процесса (<c>S102</c>). Измерено 27.08.2026 ОДНИМ И ТЕМ ЖЕ exe из
        /// двух рабочих каталогов: из своего читался положенный рядом
        /// <c>config\NuclideDefinition.xml</c>, из корня репозитория — КОРНЕВОЙ,
        /// а это другой файл на другое число записей. То есть «положить конфиг
        /// рядом с пробой» не гарантировало ничего.
        ///
        /// Приложению та же грабля стоит дороже пробы: текущий каталог меняет
        /// ЛЮБОЙ диалог открытия файла — ровно то, из-за чего читатели баз уже
        /// берут путь от каталога сборки (<c>NucBase.DataBase</c>, <c>T23</c>).
        /// После похода за спектром конфигурация уезжала туда, куда человек
        /// последний раз ходил.
        ///
        /// ⚠ Корпусные прогоны это НЕ сдвигает, и это проверено чтением
        /// оснастки, а не выведено: exe пробы КЛАДЁТСЯ В рабочий каталог и
        /// зовётся оттуда же — <c>run_appwd.ps1</c> берёт
        /// <c>wd_app\CorpusFsaProbe.exe</c> и делает туда <c>Push-Location</c>;
        /// так же устроены <c>tools/effmaker/run.ps1</c>,
        /// <c>run_peakorigin.ps1</c>, <c>tools/pie/run_corpus.ps1</c> и
        /// <c>sweep_s57.ps1</c>. Каталог сборки и текущий там один и тот же.
        ///
        /// ⚠ Ветка ClickOnce (<c>%AppData%\BecqMoni</c>) не тронута: там путь и
        /// был абсолютным.
        /// </summary>
        static string AppDir
        {
            get
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        /// <summary>
        /// Путь портативной раскладки. Замыкающий разделитель СОХРАНЯЕТСЯ:
        /// половина потребителей склеивает имя файла простым сложением строк
        /// (<c>ROIConfigManager</c>, <c>DeviceConfigManager</c>), и <c>ROI\</c>
        /// без хвостового слэша дал бы им путь в соседний каталог.
        /// </summary>
        static string Local(string relative)
        {
            return Path.Combine(AppDir, relative);
        }

        public string UserDirectory
        {
            get
            {
                if(!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni";
                }
                // Пустая строка была не «нет каталога», а «спросите текущий»:
                // единственный её читатель (`MainForm`) закрыт условием
                // `!IsStandAlone` и до неё не доходил, а `Path.GetFullPath("")`
                // бросает. Портативный пользовательский каталог — каталог
                // сборки; хвостовой разделитель снят, потому что читатель
                // приписывает к нему `\config` сам.
                return AppDir.TrimEnd(Path.DirectorySeparatorChar);
            }
        }

        public string Config
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config";
                }
                return Local("config");
            }
        }

        public string Device
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config\\device\\";
                }
                return Local("config\\device\\");
            }
        }

        public string DeviceDir
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config\\device";
                }
                return Local("config\\device");
            }
        }

        public string MainConfig
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config\\BecquerelMonitor.xml";
                }
                return Local("config\\BecquerelMonitor.xml");
            }
        }

        public string Layout
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config\\layout\\";
                }
                return Local("config\\layout\\");
            }
        }

        public string NuclideDefinition
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config\\NuclideDefinition.xml";
                }
                return Local("config\\NuclideDefinition.xml");
            }
        }

        /// <summary>
        /// Библиотека веществ конструктора геометрий (E20). Лежит рядом с
        /// остальной конфигурацией, одним файлом: веществ десятки, а не сотни,
        /// и разносить их по файлам, как ROI, незачем.
        /// </summary>
        public string GeometryMaterials
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config\\GeometryMaterials.xml";
                }
                return Local("config\\GeometryMaterials.xml");
            }
        }

        public string ROI
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config\\ROI\\";
                }
                return Local("config\\ROI\\");
            }
        }

        public string ROIDir
        {
            get
            {
                if (!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni\\config\\ROI";
                }
                return Local("config\\ROI");
            }
        }

        public string PackageVersion
        {
            get
            {
                try
                {
                    ApplicationDeployment currentDeployment = ApplicationDeployment.CurrentDeployment;
                    return currentDeployment.CurrentVersion.ToString();
                }
                catch
                {
                    System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                    return fvi.FileVersion;
                }
            }
        }


        public bool IsStandAlone
        {
            get
            {
                if (this.isStandAlone != null)
                {
                    return (bool)this.isStandAlone;
                }
                else
                {
                    this.isStandAlone = CheckStandAlone();
                    return (bool)this.isStandAlone;
                }
            }
        }


        bool? isStandAlone;

        static Package instance = new Package();
    }
}
