using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Voat.Domain.Command;

namespace Voat.Voting.Outcomes
{
  
    public abstract class VoteOutcome : VoteItem
    {
        public abstract Task<CommandResponse> Execute(IPrincipal principal);
    }
}
