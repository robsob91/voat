using System;
using System.Collections.Generic;
using System.Text;

namespace Voat.Domain.Models
{
    public class RemoveSubverseModeratorModel
    {
        public string Subverse { get; set; }
        public string UserName { get; set; }
        public string Reason { get; set; }

    }
}
