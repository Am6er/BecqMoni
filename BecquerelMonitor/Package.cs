using System;
using System.Collections.Generic;
using System.Deployment.Application;
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

        public string UserDirectory
        {
            get
            {
                if(!IsStandAlone)
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BecqMoni";
                }
                return "";
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
                return "config";
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
                return "config\\device\\";
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
                return "config\\device";
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
                return "config\\BecquerelMonitor.xml";
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
                return "config\\layout\\";
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
                return "config\\NuclideDefinition.xml";
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
                return "config\\ROI\\";
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
                return "config\\ROI";
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
