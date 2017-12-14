using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voat.Caching;
using Voat.Common;
using Voat.Configuration;
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

        public async Task<CommandResponse> AddModerator(Data.Models.SubverseModerator subverseModerator)
        {
            DemandAuthentication();

            var subverse = ToCorrectSubverseCasing(subverseModerator.Subverse);

            if (String.IsNullOrEmpty(subverse))
            {
                return CommandResponse.FromStatus(Status.Invalid, Localization.SubverseNotFound(subverse));
            }

            var userName = ToCorrectUserNameCasing(subverseModerator.UserName);
            if (String.IsNullOrEmpty(subverseModerator.UserName))
            {
                return CommandResponse.FromStatus(Status.Invalid, Localization.UserNotFound(subverseModerator.UserName));
            }


            if (!ModeratorPermission.HasPermission(User, subverseModerator.Subverse, ModeratorAction.AddModerator))
            {
                return CommandResponse.FromStatus(Status.Denied, Localization.UserNotGrantedPermission());
            }

            var subMod = new Data.Models.SubverseModerator() {
                CreatedBy = User.Identity.Name,
                CreationDate = CurrentDate,
                Power = subverseModerator.Power,
                UserName = subverseModerator.UserName,
                Subverse = subverse
            };

            _db.SubverseModerator.Add(subMod);
            await _db.SaveChangesAsync();

            return CommandResponse.FromStatus(Status.Success);

        }
        public async Task<CommandResponse> RemoveModerator(RemoveSubverseModeratorModel removeModerator)
        {
            DemandAuthentication();

            //Just send logic to main method
            var subverseModeratorRecord = _db.SubverseModerator.FirstOrDefault(x => x.Subverse == removeModerator.Subverse && x.UserName == removeModerator.UserName);
            return await RemoveModerator(subverseModeratorRecord?.ID ?? -1, true);

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
            if (!ModeratorPermission.HasPermission(User, subverse.Name, Domain.Models.ModeratorAction.RemoveModerator))
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

        #region ADD/REMOVE MODERATORS LOGIC FROM CONTROLLER

        public async Task<CommandResponse> AcceptModeratorInvitation(int invitationId)
        {
            int maximumOwnedSubs = VoatSettings.Instance.MaximumOwnedSubs;

            //TODO: These errors are not friendly - please update to redirect or something
            // check if there is an invitation for this user with this id
            var userInvitation = _db.ModeratorInvitation.Find(invitationId);
            if (userInvitation == null)
            {
                return CommandResponse.FromStatus(Status.Invalid, Localization.InvalidModInvite());
                //return ErrorView(new ErrorViewModel() { Title = "Moderator Invite Not Found", Description = "The moderator invite is no longer valid", Footer = "Where did it go?" });
            }

            // check if logged in user is actually the invited user
            if (!User.Identity.Name.IsEqual(userInvitation.Recipient))
            {
                return CommandResponse.FromStatus(Status.Denied);
                //return ErrorView(ErrorViewModel.GetErrorViewModel(HttpStatusCode.Unauthorized));
            }

            // check if user is over modding limits
            var amountOfSubsUserModerates = _db.SubverseModerator.Where(s => s.UserName.ToLower() == User.Identity.Name.ToLower());
            if (amountOfSubsUserModerates.Any())
            {
                if (amountOfSubsUserModerates.Count() >= maximumOwnedSubs)
                {
                    return CommandResponse.FromStatus(Status.Denied, $"Sorry, you can not own or moderate more than {maximumOwnedSubs} subverses.");
                    //return ErrorView(new ErrorViewModel() { Title = "Maximum Moderation Level Exceeded", Description = $"Sorry, you can not own or moderate more than {maximumOwnedSubs} subverses.", Footer = "That's too bad" });
                }
            }

            // check if subverse exists
            var subverse = _db.Subverse.FirstOrDefault(s => s.Name.ToLower() == userInvitation.Subverse.ToLower());
            if (subverse == null)
            {
                return CommandResponse.FromStatus(Status.Invalid, Localization.SubverseNotFound());
                //return ErrorView(ErrorViewModel.GetErrorViewModel(ErrorType.SubverseNotFound));
                //return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // check if user is already a moderator of this sub
            var userModerating = _db.SubverseModerator.Where(s => s.Subverse.ToLower() == userInvitation.Subverse.ToLower() && s.UserName.ToLower() == User.Identity.Name.ToLower());
            if (userModerating.Any())
            {
                _db.ModeratorInvitation.Remove(userInvitation);
                _db.SaveChanges();
                return CommandResponse.FromStatus(Status.Invalid, $"You are currently already a moderator of this subverse");
                //return ErrorView(new ErrorViewModel() { Title = "You = Moderator * 2?", Description = "You are currently already a moderator of this subverse", Footer = "How much power do you want?" });
            }

            // add user as moderator as specified in invitation
            var subAdm = new Data.Models.SubverseModerator
            {
                Subverse = subverse.Name,
                UserName = UserHelper.OriginalUsername(userInvitation.Recipient),
                Power = userInvitation.Power,
                CreatedBy = UserHelper.OriginalUsername(userInvitation.CreatedBy),
                CreationDate = Repository.CurrentDate
            };

            _db.SubverseModerator.Add(subAdm);

            // notify sender that user has accepted the invitation
            var message = new Domain.Models.SendMessage()
            {
                Sender = $"v/{subverse.Name}",
                Subject = $"Moderator invitation for v/{subverse.Name} accepted",
                Recipient = userInvitation.CreatedBy,
                Message = $"User {User.Identity.Name} has accepted your invitation to moderate subverse v/{subverse.Name}."
            };
            var cmd = new SendMessageCommand(message).SetUserContext(User);
            await cmd.Execute();

            //clear mod cache
            CacheHandler.Instance.Remove(CachingKey.SubverseModerators(subverse.Name));

            // delete the invitation from database
            _db.ModeratorInvitation.Remove(userInvitation);
            _db.SaveChanges();

            return CommandResponse.Successful();
            //return RedirectToAction("Update", "SubverseModeration", new { subverse = subverse.Name });
        }
        
        public async Task<CommandResponse> InviteModerator(Data.Models.SubverseModerator subverseAdmin)
        {
            //if (!ModelState.IsValid)
            //{
            //    return View(subverseAdmin);
            //}

            // check if caller can add mods, if not, deny posting
            if (!ModeratorPermission.HasPermission(User, subverseAdmin.Subverse, Domain.Models.ModeratorAction.InviteModerator))
            {
                return CommandResponse.FromStatus(Status.Denied, Localization.UserNotGrantedPermission());
                //return RedirectToAction("Index", "Home");
            }

            subverseAdmin.UserName = subverseAdmin.UserName.TrimSafe();
            Data.Models.Subverse subverseModel = null;

            ////lots of premature retuns so wrap the common code
            //var sendFailureResult = new Func<string, CommandResponse>(errorMessage =>
            //{
            //    ViewBag.SubverseModel = subverseModel;
            //    ViewBag.SubverseName = subverseAdmin.Subverse;
            //    ViewBag.SelectedSubverse = string.Empty;
            //    ModelState.AddModelError(string.Empty, errorMessage);
            //    SetNavigationViewModel(subverseAdmin.Subverse);

            //    return View("~/Views/Subverses/Admin/AddModerator.cshtml",
            //    new SubverseModeratorViewModel
            //    {
            //        UserName = subverseAdmin.UserName,
            //        Power = subverseAdmin.Power
            //    }
            //    );
            //});

            // prevent invites to the current moderator
            if (User.Identity.Name.IsEqual(subverseAdmin.UserName))
            {
                return CommandResponse.FromStatus(Status.Denied, "Can not add yourself as a moderator");
            }

            string originalRecipientUserName = UserHelper.OriginalUsername(subverseAdmin.UserName);
            // prevent invites to the current moderator
            if (String.IsNullOrEmpty(originalRecipientUserName))
            {
                return CommandResponse.FromStatus(Status.Denied, "User can not be found");
            }

            // get model for selected subverse
            subverseModel = DataCache.Subverse.Retrieve(subverseAdmin.Subverse);
            if (subverseModel == null)
            {
                return CommandResponse.FromStatus(Status.Invalid, Localization.SubverseNotFound());
                //return ErrorView(ErrorViewModel.GetErrorViewModel(ErrorType.SubverseNotFound));
            }

            if ((subverseAdmin.Power < 1 || subverseAdmin.Power > 4) && subverseAdmin.Power != 99)
            {
                return CommandResponse.FromStatus(Status.Denied, "Only powers levels 1 - 4 and 99 are supported currently");
            }

            //check current mod level and invite level and ensure they are a lower level
            var currentModLevel = ModeratorPermission.Level(User, subverseModel.Name);
            if (subverseAdmin.Power <= (int)currentModLevel && currentModLevel != Domain.Models.ModeratorLevel.Owner)
            {
                return CommandResponse.FromStatus(Status.Denied, "Sorry, but you can only add moderators that are a lower level than yourself");
            }

            int maximumOwnedSubs = VoatSettings.Instance.MaximumOwnedSubs;

            // check if the user being added is not already a moderator of 10 subverses
            var currentlyModerating = _db.SubverseModerator.Where(a => a.UserName == originalRecipientUserName).ToList();

            //SubverseModeratorViewModel tmpModel;
            if (currentlyModerating.Count <= maximumOwnedSubs)
            {
                // check that user is not already moderating given subverse
                var isAlreadyModerator = _db.SubverseModerator.FirstOrDefault(a => a.UserName == originalRecipientUserName && a.Subverse == subverseAdmin.Subverse);

                if (isAlreadyModerator == null)
                {
                    // check if this user is already invited
                    var userModeratorInvitations = _db.ModeratorInvitation.Where(i => i.Recipient.ToLower() == originalRecipientUserName.ToLower() && i.Subverse.ToLower() == subverseModel.Name.ToLower());
                    if (userModeratorInvitations.Any())
                    {
                        return CommandResponse.FromStatus(Status.Denied, "Sorry, the user is already invited to moderate this subverse");
                    }

                    // send a new moderator invitation
                    Data.Models.ModeratorInvitation modInv = new Data.Models.ModeratorInvitation
                    {
                        CreatedBy = User.Identity.Name,
                        CreationDate = Repository.CurrentDate,
                        Recipient = originalRecipientUserName,
                        Subverse = subverseAdmin.Subverse,
                        Power = subverseAdmin.Power
                    };

                    _db.ModeratorInvitation.Add(modInv);
                    _db.SaveChanges();
                    int invitationId = modInv.ID;
                    var invitationBody = new StringBuilder();

                    //v/{subverse}/about/moderatorinvitations/accept/{invitationId}

                    string acceptInviteUrl = VoatUrlFormatter.BuildUrlPath(null, new PathOptions(true, true), $"/v/{subverseModel.Name}/about/moderatorinvitations/accept/{invitationId}");

                    invitationBody.Append("Hello,");
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append($"@{User.Identity.Name} invited you to moderate v/" + subverseAdmin.Subverse + ".");
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append($"Please visit the following link if you want to accept this invitation: {acceptInviteUrl}");
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append("Thank you.");

                    var cmd = new SendMessageCommand(new Domain.Models.SendMessage()
                    {
                        Sender = $"v/{subverseAdmin.Subverse}",
                        Recipient = originalRecipientUserName,
                        Subject = $"v/{subverseAdmin.Subverse} moderator invitation",
                        Message = invitationBody.ToString()
                    }, true).SetUserContext(User);
                    await cmd.Execute();

                    return CommandResponse.Successful();
                }
                else
                {
                    return CommandResponse.FromStatus(Status.Denied, "Sorry, the user is already moderating this subverse");
                }
            }
            else
            {
                return CommandResponse.FromStatus(Status.Denied, "Sorry, the user is already moderating a maximum of " + maximumOwnedSubs + " subverses");
            }
        }
        
        #endregion ADD/REMOVE MODERATORS LOGIC
    }
}
