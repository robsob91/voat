using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voat.Common;
using Voat.Domain.Command;
using Voat.Domain.Models;
using Voat.Utilities;

namespace Voat.Data
{
    public partial class Repository
    {
        public async Task<CommandResponse<string>> RegenerateThumbnail(int submissionID)
        {
            DemandAuthentication();

            // get model for selected submission
            var submission = _db.Submission.Find(submissionID);
            var response = CommandResponse.FromStatus(Status.Error);

            if (submission == null || submission.IsDeleted)
            {
                return CommandResponse.FromStatus("", Status.Error, "Submission is missing or deleted");
            }
            var subverse = submission.Subverse;

            // check if caller is subverse moderator, if not, deny change
            if (!ModeratorPermission.HasPermission(User, subverse, Domain.Models.ModeratorAction.AssignFlair))
            {
                return CommandResponse.FromStatus("", Status.Denied, "Moderator Permissions are not satisfied");
            }
            try
            {
                throw new NotImplementedException();

                await _db.SaveChangesAsync();

                return CommandResponse.FromStatus("", Status.Success);
            }
            catch (Exception ex)
            {
                return CommandResponse.Error<CommandResponse<string>>(ex);
            }
        }
        public async Task<CommandResponse<bool>> ToggleNSFW(int submissionID)
        {
            DemandAuthentication();

            // get model for selected submission
            var submission = _db.Submission.Find(submissionID);
            var response = CommandResponse.FromStatus(Status.Error);

            if (submission == null || submission.IsDeleted)
            {
                return CommandResponse.FromStatus(false, Status.Error, "Submission is missing or deleted");
            }
            var subverse = submission.Subverse;

            if (!User.Identity.Name.IsEqual(submission.UserName))
            {
                // check if caller is subverse moderator, if not, deny change
                if (!ModeratorPermission.HasPermission(User, subverse, Domain.Models.ModeratorAction.AssignFlair))
                {
                    return CommandResponse.FromStatus(false, Status.Denied, "Moderator Permissions are not satisfied");
                }
            }
            try
            {
                submission.IsAdult = !submission.IsAdult;
                
                await _db.SaveChangesAsync();

                return CommandResponse.FromStatus(submission.IsAdult, Status.Success);
            }
            catch (Exception ex)
            {
                return CommandResponse.Error<CommandResponse<bool>>(ex);
            }
        }
        public async Task<CommandResponse> ToggleSticky(int submissionID, string subverse = null, bool clearExisting = false, int stickyLimit = 3)
        {
            DemandAuthentication();

            // get model for selected submission
            var submission = _db.Submission.Find(submissionID);
            var response = CommandResponse.FromStatus(Status.Error);


            if (submission == null || submission.IsDeleted)
            {
                return CommandResponse.FromStatus(Status.Error, "Submission is missing or deleted");
            }
            //Eventually we want users to be able to sticky other subs posts, but for now make sure we don't allow this
            subverse = submission.Subverse;

            // check if caller is subverse moderator, if not, deny change
            if (!ModeratorPermission.HasPermission(User, subverse, Domain.Models.ModeratorAction.AssignStickies))
            {
                return CommandResponse.FromStatus(Status.Denied, "Moderator Permissions are not satisfied");
            }
            int affectedCount = 0;
            try
            {
                // find and clear current sticky if toggling
                var existingSticky = _db.StickiedSubmission.FirstOrDefault(s => s.SubmissionID == submissionID);
                if (existingSticky != null)
                {
                    _db.StickiedSubmission.Remove(existingSticky);
                    affectedCount += -1;
                }
                else
                {
                    if (clearExisting)
                    {
                        // remove all stickies for subverse matching submission subverse
                        _db.StickiedSubmission.RemoveRange(_db.StickiedSubmission.Where(s => s.Subverse == subverse));
                        affectedCount = 0;
                    }

                    // set new submission as sticky
                    var stickyModel = new Data.Models.StickiedSubmission
                    {
                        SubmissionID = submissionID,
                        CreatedBy = User.Identity.Name,
                        CreationDate = Repository.CurrentDate,
                        Subverse = subverse
                    };

                    _db.StickiedSubmission.Add(stickyModel);
                    affectedCount += 1;
                }

                //limit sticky counts 
                var currentCount = _db.StickiedSubmission.Count(x => x.Subverse == subverse);
                if ((currentCount + affectedCount) > stickyLimit)
                {
                    return CommandResponse.FromStatus(Status.Denied, $"Stickies are limited to {stickyLimit}");
                }

                await _db.SaveChangesAsync();

                StickyHelper.ClearStickyCache(submission.Subverse);

                return CommandResponse.FromStatus(Status.Success);
            }
            catch (Exception ex)
            {
                return CommandResponse.Error<CommandResponse>(ex);
            }
        }

        public async Task<CommandResponse<Comment>> DistinguishComment(int commentID)
        {
            DemandAuthentication();
            var response = CommandResponse.FromStatus<Comment>(null, Status.Invalid);
            var comment = await this.GetComment(commentID);

            if (comment != null)
            {
                // check to see if request came from comment author
                if (User.Identity.Name == comment.UserName)
                {
                    // check to see if comment author is also sub mod or sub admin for comment sub
                    if (ModeratorPermission.HasPermission(User, comment.Subverse, ModeratorAction.DistinguishContent))
                    {
                        var m = new DapperMulti();

                        var u = new DapperUpdate();
                        //u.Update = $"{SqlFormatter.Table("Comment")} SET \"IsDistinguished\" = {SqlFormatter.ToggleBoolean("\"IsDistinguished\"")}";
                        u.Update = SqlFormatter.UpdateSetBlock($"\"IsDistinguished\" = {SqlFormatter.ToggleBoolean("\"IsDistinguished\"")}", SqlFormatter.Table("Comment"));
                        u.Where = "\"ID\" = @id";
                        u.Parameters.Add("id", commentID);
                        m.Add(u);

                        var s = new DapperQuery();
                        s.Select = $"\"IsDistinguished\" FROM {SqlFormatter.Table("Comment")}";
                        s.Where = "\"ID\" = @id";
                        m.Add(s);

                        //ProTip: The actual execution of code is important.
                        var result = await _db.Connection.ExecuteScalarAsync<bool>(m.ToCommandDefinition());
                        comment.IsDistinguished = result;

                        response = CommandResponse.FromStatus(comment, Status.Success);
                    }
                    else
                    {
                        response.Message = "User does not have permissions to distinquish content";
                        response.Status = Status.Denied;
                    }
                }
                else
                {
                    response.Message = "User can only distinquish owned content";
                    response.Status = Status.Denied;
                }
            }
            else
            {
                response.Message = "Comment can not be found";
                response.Status = Status.Denied;
            }
            return response;
        }
        public Task<IEnumerable<Models.SubverseFlair>> GetSubverseFlair(string subverse)
        {

            var subverseLinkFlairs = _db.SubverseFlair
                .Where(n => n.Subverse == subverse)
                .OrderBy(s => s.Label).ToList();

            return Task.FromResult(subverseLinkFlairs.AsEnumerable());
        }

        public async Task<CommandResponse> AddModerator(Voat.Data.Models.SubverseModerator subverseModerator)
        {
            DemandAuthentication();
            return CommandResponse.FromStatus(Status.NotProcessed);

        }
        public async Task<CommandResponse> RemoveModerator(RemoveSubverseModeratorModel removeModerator)
        {
            DemandAuthentication();
            return CommandResponse.FromStatus(Status.NotProcessed);

        }
        public async Task<CommandResponse<RemoveModeratorResponse>> RemoveModerator(int subverseModeratorRecordID, bool allowSelfRemovals)
        {
            DemandAuthentication();

            var response = new RemoveModeratorResponse();
            var originUserName = User.Identity.Name;

            // get moderator name for selected subverse
            var subModerator = await _db.SubverseModerator.FindAsync(subverseModeratorRecordID).ConfigureAwait(CONSTANTS.AWAIT_CAPTURE_CONTEXT);
            if (subModerator == null)
            {
                return new CommandResponse<RemoveModeratorResponse>(response, Status.Invalid, "Can not find record");
            }

            //Set response data
            response.SubverseModerator = subModerator;
            response.OriginUserName = originUserName;
            response.TargetUserName = subModerator.UserName;
            response.Subverse = subModerator.Subverse;

            var subverse = GetSubverseInfo(subModerator.Subverse);
            if (subverse == null)
            {
                return new CommandResponse<RemoveModeratorResponse>(response, Status.Invalid, "Can not find subverse");
            }

            // check if caller has clearance to remove a moderator
            if (!ModeratorPermission.HasPermission(User, subverse.Name, Domain.Models.ModeratorAction.RemoveMods))
            {
                return new CommandResponse<RemoveModeratorResponse>(response, Status.Denied, "User does not have permissions to execute action");
            }

            var allowRemoval = false;
            var errorMessage = "Rules do not allow removal";

            if (allowSelfRemovals && originUserName.IsEqual(subModerator.UserName))
            {
                allowRemoval = true;
            }
            else if (subModerator.UserName.IsEqual("system"))
            {
                allowRemoval = false;
                errorMessage = "System moderators can not be removed or they get sad";
            }
            else
            {
                //Determine if removal is allowed:
                //Logic:
                //L1: Can remove L1's but only if they invited them / or they were added after them
                var currentModLevel = ModeratorPermission.Level(User, subverse.Name).Value; //safe to get value as previous check ensures is mod
                var targetModLevel = (ModeratorLevel)subModerator.Power;

                switch (currentModLevel)
                {
                    case ModeratorLevel.Owner:
                        if (User.IsInAnyRole(new[] { UserRole.GlobalAdmin, UserRole.Admin }))
                        {
                            allowRemoval = true;
                        }
                        else
                        {
                            if (targetModLevel == ModeratorLevel.Owner)
                            {
                                var isTargetOriginalMod = (String.IsNullOrEmpty(subModerator.CreatedBy) && !subModerator.CreationDate.HasValue); //Currently original mods have these fields nulled
                                if (isTargetOriginalMod)
                                {
                                    allowRemoval = false;
                                    errorMessage = "The creator can not be destroyed";
                                }
                                else
                                {
                                    //find current mods record
                                    var originModeratorRecord = _db.SubverseModerator.FirstOrDefault(x =>
                                    x.Subverse.ToLower() == subModerator.Subverse.ToLower()
                                    && x.UserName.ToLower() == originUserName.ToLower());

                                    if (originModeratorRecord == null)
                                    {
                                        allowRemoval = false;
                                        errorMessage = "Something seems fishy";
                                    }
                                    else
                                    {
                                        //Creators of subs have no creation date so set it low
                                        var originModCreationDate = (originModeratorRecord.CreationDate.HasValue ? originModeratorRecord.CreationDate.Value : new DateTime(2000, 1, 1));

                                        allowRemoval = (originModCreationDate < subModerator.CreationDate);
                                        errorMessage = "Moderator has seniority. Oldtimers can't be removed by a young'un";
                                    }
                                }
                            }
                            else
                            {
                                allowRemoval = true;
                            }
                        }
                        break;

                    default:
                        allowRemoval = (targetModLevel > currentModLevel);
                        errorMessage = "Only moderators at a lower level can be removed";
                        break;
                }
            }

            //ensure mods can only remove mods that are a lower level than themselves
            if (allowRemoval)
            {
                // execute removal
                _db.SubverseModerator.Remove(subModerator);
                await _db.SaveChangesAsync().ConfigureAwait(CONSTANTS.AWAIT_CAPTURE_CONTEXT);

                ////clear mod cache
                //CacheHandler.Instance.Remove(CachingKey.SubverseModerators(subverse.Name));

                return new CommandResponse<RemoveModeratorResponse>(response, Status.Success, String.Empty);
            }
            else
            {
                return new CommandResponse<RemoveModeratorResponse>(response, Status.Denied, errorMessage);
            }
        }
        
    }
}
