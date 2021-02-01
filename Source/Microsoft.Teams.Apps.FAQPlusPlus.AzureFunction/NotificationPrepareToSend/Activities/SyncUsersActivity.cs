﻿// <copyright file="SyncGroupMembersActivity.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Prep.Func.PreparingToSend
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Extensions.DurableTask;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;
    using Microsoft.Graph;
    using Microsoft.Teams.Apps.FAQPlusPlus.AzureFunctionCommon.Extensions;
    using Microsoft.Teams.Apps.FAQPlusPlus.AzureFunctionCommon.Repositories.NotificationData;
    using Microsoft.Teams.Apps.FAQPlusPlus.AzureFunctionCommon.Repositories.SentNotificationData;
    using Microsoft.Teams.Apps.FAQPlusPlus.AzureFunctionCommon.Repositories.UserData;
    using Microsoft.Teams.Apps.FAQPlusPlus.AzureFunction.NotificationPrepareToSend;
    using Microsoft.Teams.Apps.FAQPlusPlus.AzureFunction.NotificationPrepareToSend.Extensions;
    using Microsoft.Teams.Apps.FAQPlusPlus.AzureFunctionCommon.Resources;
    using Microsoft.Azure.Cosmos.Table;

    /// <summary>
    /// Syncs group members to Sent notification table.
    /// </summary>
    public class SyncUsersActivity
    {
        private readonly NotificationDataRepository notificationDataRepository;
        private readonly SentNotificationDataRepository sentNotificationDataRepository;
        private readonly UserDataRepository userDataRepository;
        private readonly IStringLocalizer<Strings> localizer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncUsersActivity"/> class.
        /// </summary>
        /// <param name="sentNotificationDataRepository">Sent notification data repository.</param>
        /// <param name="notificationDataRepository">Notifications data repository.</param>
        /// <param name="groupMembersService">Group members service.</param>
        /// <param name="userDataRepository">User Data repository.</param>
        /// <param name="localizer">Localization service.</param>
        public SyncUsersActivity(
            SentNotificationDataRepository sentNotificationDataRepository,
            NotificationDataRepository notificationDataRepository,
            UserDataRepository userDataRepository,
            IStringLocalizer<Strings> localizer)
        {
            this.notificationDataRepository = notificationDataRepository ?? throw new ArgumentNullException(nameof(notificationDataRepository));
            this.sentNotificationDataRepository = sentNotificationDataRepository ?? throw new ArgumentNullException(nameof(sentNotificationDataRepository));
            this.userDataRepository = userDataRepository ?? throw new ArgumentNullException(nameof(userDataRepository));
            this.localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Syncs group members to Sent notification table.
        /// </summary>
        /// <param name="input">Input.</param>
        /// <param name="log">Logging service.</param>
        /// <returns>It returns the group transitive members first page and next page url.</returns>
        [FunctionName(FunctionNames.SyncUsersActivity)]
        public async Task RunAsync(
        [ActivityTrigger](string notificationId, IEnumerable<string> userPcns) input, ILogger log)
        {
            var notificationId = input.notificationId;
            var userPcns = input.userPcns;

            try
            {
                // Convert to Recipients
                var recipients = await this.GetRecipientsAsync(notificationId, userPcns);

                // Store.
                await this.sentNotificationDataRepository.BatchInsertOrMergeAsync(recipients);
            }
            catch (Exception ex)
            {
                var errorMessage = this.localizer.GetString("FailedToGetMembersForGroupFormat", userPcns, ex.Message);
                log.LogError(ex, errorMessage);
                await this.notificationDataRepository.SaveWarningInNotificationDataEntityAsync(notificationId, errorMessage);
            }
        }

        /// <summary>
        /// Reads corresponding user entity from User table and creates a recipient for every user.
        /// </summary>
        /// <param name="notificationId">Notification Id.</param>
        /// <param name="usePcns">Users.</param>
        /// <returns>List of recipients.</returns>
        private async Task<IEnumerable<SentNotificationDataEntity>> GetRecipientsAsync(string notificationId, IEnumerable<string> usePcns)
        {
            var recipients = new ConcurrentBag<SentNotificationDataEntity>();

            // Get User Entities.
            var maxParallelism = Math.Min(100, usePcns.Count());
            await Task.WhenAll(usePcns.ForEachAsync(maxParallelism, async userPcn =>
            {
                var filter = TableQuery.GenerateFilterCondition(nameof(UserDataEntity.Upn), QueryComparisons.Equal, userPcn);
                var userEntity = (await this.userDataRepository.GetWithFilterAsync(filter)).FirstOrDefault();
                if (userEntity == null)
                {
                    userEntity = new UserDataEntity()
                    {
                        Upn = userPcn,
                    };
                }

                recipients.Add(userEntity.CreateInitialSentNotificationDataEntity(partitionKey: notificationId));
            }));

            return recipients;
        }
    }
}
