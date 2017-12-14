using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Voat.Data;
using Voat.Data.Models;
using Voat.Domain.Models;

namespace Voat.Domain.Command
{
    public class RemoveModeratorCommand : ModifyModeratorCommand
    {
        private RemoveSubverseModeratorModel _model;

        public RemoveModeratorCommand(RemoveSubverseModeratorModel model)
        {
            _model = model;
        }

        protected override async Task<CommandResponse> ProtectedExecute()
        {
            using (var repo = new Repository(User))
            {
                return await repo.RemoveModerator(_model);
            }
        }
    }
}
