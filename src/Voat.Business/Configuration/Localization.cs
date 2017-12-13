using System;
using System.Collections.Generic;
using System.Text;

namespace Voat.Configuration
{
    //Start of localization for error messages
    public static class Localization
    {
        public static string SubverseNotFound(string subverse = null)
        {
            if (String.IsNullOrEmpty(subverse))
            {
                return "Subverse not found";
            }
            else
            {
                return $"Subverse '{0}' not found";
            }
        }
        public static string UserNotGrantedPermission()
        {
            return "User does not have permissions to execute action";
        }

        internal static string InvalidModInvite()
        {
            return "The moderator invite is not valid";
        }

        internal static string UserNotFound(string userName = null)
        {
            if (String.IsNullOrEmpty(userName))
            {
                return "User not found";
            }
            else
            {
                return $"User '{0}' not found";
            }
        }
    }
}
