namespace Sitecore.Support.Notifications
{
    using System;
    using Configuration;
    using Events;
    using Sitecore.Data.Clones;
    using Sitecore.Data.DataProviders.SqlServer;
    using Sitecore.Data.Events;
    using Sitecore.Data.Items;

    public class ProcessNotifications
    {
        public void Process(object sender, EventArgs args)
        {
            Item item = Event.ExtractParameter(args, 0) as Item;

            if (item.IsClone == true)
            {
                return;
            }

            var clones = item.GetClones();

            if (clones != null)
            {
                foreach (var clone in clones)
                {
                    if (Settings.ItemCloning.ForceUpdate)
                    {
                        var  newClone = clone.Versions.GetLatestVersion(clone.Uri.Language);
                        var notifications = item.Database.NotificationProvider.GetNotifications(newClone);
                        using (new EventDisabler())
                        {
                            if (notifications != null)
                            {
                                foreach (var notification in notifications)
                                {
                                    VersionAddedNotification n = notification as VersionAddedNotification;

                                    if (n != null)
                                    {
                                        n.ForceAccept = true;
                                    }

                                    notification.Accept(newClone);
                                }
                            }
                        }

                    }
                }
            }
        }
    }
}
