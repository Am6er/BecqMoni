using System;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceConfig
    {
        [XmlIgnore]
        public string RestAuthSecret
        {
            get
            {
                if (string.IsNullOrEmpty(RestAuthSecretProtected))
                    return "";

                try
                {
                    byte[] bytes = ProtectedData.Unprotect(
                        Convert.FromBase64String(RestAuthSecretProtected),
                        null,
                        DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch (CryptographicException)
                {
                    AtomSpectraTcpLog.Warn("Session", "stored REST secret cannot be decrypted for this Windows user; enter it again");
                    return "";
                }
                catch (FormatException)
                {
                    AtomSpectraTcpLog.Warn("Session", "stored REST secret cannot be decrypted for this Windows user; enter it again");
                    return "";
                }
            }
            set
            {
                RestAuthSecretProtected = string.IsNullOrEmpty(value)
                    ? ""
                    : Convert.ToBase64String(ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(value),
                        null,
                        DataProtectionScope.CurrentUser));
            }
        }

        public string EffectiveRestUrl()
        {
            string url = (RestUrl ?? "").Trim();
            if (url.Length > 0)
                return url;

            string portPart = restPort == 80 ? "" : ":" + restPort;
            return "http://" + host + portPart + "/";
        }

        public AtomSpectraRestClient CreateRestClient(out string error)
        {
            error = null;
            string url = EffectiveRestUrl();

            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)
                || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(u.Host) || !string.IsNullOrEmpty(u.UserInfo))   // no user:pass in the URL
            {
                error = "invalid board URL (user:password in a URL is not allowed, use the authorization fields): " + AtomSpectraTcpLog.Mask(url);
                return null;
            }

            AtomSpectraRestClient c = new AtomSpectraRestClient(url);
            c.UseSystemProxy = !RestNoProxy;
            c.AuthKind = RestAuthKind;
            c.AuthUser = RestAuthUser;
            c.AuthSecret = RestAuthKind == AtomSpectraRestAuth.None ? null : RestAuthSecret;
            return c;
        }

        // What makes the controller open a new REST session (in memory only, never logged).
        public string RestSessionKey()
        {
            return EffectiveRestUrl() + "|" + RestPollSeconds + "|" + RestObserveOnly + "|" + RestLimitSeconds + "|" + RestNoProxy
                + "|" + RestAuthKind + "|" + RestAuthUser + "|" + (RestAuthSecret ?? "").GetHashCode();   // not the DPAPI text, it differs on every save
        }

        public static bool IsValidRestUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true;

            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u)
                && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrEmpty(u.Host) && string.IsNullOrEmpty(u.UserInfo);
        }
    }
}
