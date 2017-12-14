using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Voat.Data;
using Voat.Data.Models;

namespace Voat.Domain.Command
{
    public class AddModeratorCommand : ModifyModeratorCommand
    {
        public SubverseModerator _model;
        public AddModeratorCommand(SubverseModerator model)
        {
            _model = model;
        }

        protected override async Task<CommandResponse> ProtectedExecute()
        {
            using (var repo = new Repository(User))
            {
                return await repo.AddModerator(_model);
            }
        }
    }
}
