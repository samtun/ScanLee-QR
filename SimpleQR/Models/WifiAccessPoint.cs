namespace SimpleQR.Models
{
    public class WifiAccessPoint
    {
        public string Ssid { get; private set; }
        public string Password { get; private set; }
        public string SecurityType { get; private set; }
        public bool IsHidden { get; private set; }

        public WifiAccessPoint(string ssid, string password, string securityType, bool isHidden)
        {
            Ssid = ssid;
            Password = password;
            SecurityType = securityType;
            IsHidden = isHidden;
        }
    }
}
