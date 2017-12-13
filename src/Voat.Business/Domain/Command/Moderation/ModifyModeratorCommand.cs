using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Voat.Domain.Command
{
    public abstract class ModifyModeratorCommand : Domain.Command.Command<CommandResponse>
    {
        protected override Task<CommandResponse> ExecuteStage(CommandStage stage, CommandResponse previous)
        {
            switch (stage)
            {
                case CommandStage.OnValidation:

                    break;
            }

            return base.ExecuteStage(stage, previous);
        }
    }
}
