using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using collablio.Models;

namespace collablio
{
	class Helpers
	{
		public static string GetValueOrBlank(string key, Dictionary<string,string> dict)
		{
			string retVal;
			if(!dict.TryGetValue(key, out retVal))
				retVal = "";
			return retVal; 
		}

		private static HMACSHA256 _hs256 = new HMACSHA256();
		
		internal static readonly char[] chars =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray(); 
				
        public static string GetUniqueKey(int size)
        {            
            byte[] data = new byte[4*size];
            using (RNGCryptoServiceProvider crypto = new RNGCryptoServiceProvider())
            {
                crypto.GetBytes(data);
            }
            StringBuilder result = new StringBuilder(size);
            for (int i = 0; i < size; i++)
            {
                var rnd = BitConverter.ToUInt32(data, i * 4);
                var idx = rnd % chars.Length;

                result.Append(chars[idx]);
            }
            return result.ToString();
        }
		
		public static string GetB64EncodedHS256FromString(string input)
		{
			byte[] hashValue = _hs256.ComputeHash(Encoding.UTF8.GetBytes(input));
			string base64hmac = Convert.ToBase64String(hashValue);
			return base64hmac;
		}
		
		public static ulong UIDToUlong(string uid)
		{
			try
			{
				return Convert.ToUInt64(uid,16);
			}
			catch (Exception)
			{
				return 0;
			}
		}

		public static string UlongToUID(ulong uidl)
		{
			return $"0x{uidl:X}";
		}

		public static string SanitiseUID(string uid)
		{
			return UlongToUID(UIDToUlong(uid));
		}
		
		private static HashSet<string> txtContentTypes = new HashSet<string> { "application/xml", "application/json", Node.ATTACH_EMPTY };

		public static bool IsTextContentType(string contentType)
		{
			return contentType.StartsWith("text/") || txtContentTypes.Contains(contentType);
		}

		public static bool IsImageContentType(string contentType)
		{
			return contentType.StartsWith("image/");
		}

		//static helper methods
		public static DateTime UnixEpochToDateTime(double unixEpochTimestamp)
		{
			DateTime dtDateTime = new DateTime(1970,1,1,0,0,0,0,DateTimeKind.Utc);
			dtDateTime = dtDateTime.AddSeconds( unixEpochTimestamp ).ToUniversalTime();
			return dtDateTime;
		}

		public static double DateTimeToUnixEpoch(DateTime dateTimeObj)
		{
			return (double)(dateTimeObj.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
		}

	}
}