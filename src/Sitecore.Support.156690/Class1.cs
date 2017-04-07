namespace Sitecore.Support.Data.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Collections;
    using Configuration;
    using Diagnostics;
    using Events;
    using Globalization;
    using Links;
    using SecurityModel;
    using Sitecore.Data;
    using Sitecore.Data.Clones;
    using Sitecore.Data.Engines.DataCommands;
    using Sitecore.Data.Events;
    using Sitecore.Data.Items;
    using Sitecore.Data.Managers;
    using Sitecore.Data.Templates;


    /// <summary>Defines the notification manager class.</summary>
    public class NotificationManager
    {
        /// <summary>Global lock.</summary>
        private static readonly object globalLock = new object();
        /// <summary>The initialized flag</summary>
        private static bool _initialized;

        /// <summary>Initializes this instance.</summary>
        public static void Initialize()
        {
            if (NotificationManager._initialized)
                return;
            lock (NotificationManager.globalLock)
            {
                if (NotificationManager._initialized)
                    return;
                try
                {
                    foreach (Database item_0 in Factory.GetDatabases())
                        NotificationManager.AttachEvents(item_0);
                    Database.InstanceCreated += new EventHandler<InstanceCreatedEventArgs>(NotificationManager.Database_InstanceCreated);
                    Event.Subscribe("item:templateChanged", new EventHandler(NotificationManager.TemplateChangedHandler));
                }
                finally
                {
                    NotificationManager._initialized = true;
                }
            }
        }

        /// <summary>Attaches the events.</summary>
        /// <param name="database">The database.</param>
        private static void AttachEvents(Database database)
        {
            Assert.ArgumentNotNull((object)database, "database");
            if (database.NotificationProvider == null)
                return;
            database.Engines.DataEngine.SavedItem += new EventHandler<ExecutedEventArgs<SaveItemCommand>>(NotificationManager.DataEngine_SavedItem);
            database.Engines.DataEngine.CreatedItem += new EventHandler<ExecutedEventArgs<CreateItemCommand>>(NotificationManager.DataEngine_CreatedItem);
            database.Engines.DataEngine.AddedVersion += new EventHandler<ExecutedEventArgs<AddVersionCommand>>(NotificationManager.DataEngine_AddedVersion);
            database.Engines.DataEngine.MovedItem += new EventHandler<ExecutedEventArgs<MoveItemCommand>>(NotificationManager.DataEngine_MovedItem);
            database.Engines.DataEngine.DeletedItem += new EventHandler<ExecutedEventArgs<DeleteItemCommand>>(NotificationManager.DataEngine_DeletedItem);
            database.Engines.DataEngine.RemovedVersion += new EventHandler<ExecutedEventArgs<RemoveVersionCommand>>(NotificationManager.DataEngine_RemoveVersion);
        }

        /// <summary>
        /// Handles the DeletedItem event of the DataEngine control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The instance containing the event data.</param>
        private static void DataEngine_DeletedItem(object sender, ExecutedEventArgs<DeleteItemCommand> e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull((object)e, "e");
            Item parent = e.Command.Item.Database.GetItem(e.Command.ParentId);
            if (parent == null)
                return;
            NotificationManager.HandleChildRemoved(parent, e.Command.Item.ID);
        }

        /// <summary>
        /// Handles the RemoveVersion event of the Database control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The instance containing the event data.</param>
        private static void DataEngine_RemoveVersion(object sender, ExecutedEventArgs<RemoveVersionCommand> e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull((object)e, "e");
            if (e.Command == null || e.Command.Item == null)
                return;
            Item source = e.Command.Item;
            IEnumerable<Item> allClones = NotificationManager.GetAllClones(source);
            List<ID> idList = new List<ID>();
            foreach (Item clone in allClones)
            {
                if (!idList.Contains(clone.ID) && clone.Database.NotificationProvider != null)
                {
                    foreach (Notification notification in new List<Notification>(clone.Database.NotificationProvider.GetNotifications(clone)))
                    {
                        VersionAddedNotification addedNotification = notification as VersionAddedNotification;
                        if (addedNotification != null && addedNotification.VersionUri == source.Uri && !addedNotification.Processed)
                            clone.Database.NotificationProvider.RemoveNotification(addedNotification.ID);
                    }
                }
            }
        }

        /// <summary>Handles the MovedItem event of the Database control.</summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The instance containing the event arguments.</param>
        private static void DataEngine_MovedItem(object sender, ExecutedEventArgs<MoveItemCommand> e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull((object)e, "e");
            Assert.IsNotNull((object)e.Command.Destination, "new parent item");
            Assert.IsNotNull((object)e.Command.Item, "item");
            if (!(e.Command.OldParentId != e.Command.Destination.ID))
                return;
            NotificationManager.HandleItemMovedEvent(e.Command.Destination, e.Command.Item, e.Command.OldParentId);
        }

        /// <summary>
        /// Handles the InstanceCreated event of the Database control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">
        /// The <see cref="T:Sitecore.Data.Events.InstanceCreatedEventArgs" /> instance containing the event data.
        /// </param>
        private static void Database_InstanceCreated(object sender, InstanceCreatedEventArgs e)
        {
            Assert.ArgumentNotNull((object)e, "e");
            NotificationManager.AttachEvents(e.Database);
        }

        /// <summary>The add version event.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        private static void DataEngine_AddedVersion(object sender, ExecutedEventArgs<AddVersionCommand> e)
        {
            Assert.ArgumentNotNull((object)e, "e");
            Item latestVersion = e.Command.Item.Versions.GetLatestVersion();
            if (latestVersion == null)
                return;
            IEnumerable<Item> allClones = NotificationManager.GetAllClones(latestVersion);
            Set<ID> set = new Set<ID>();
            if (latestVersion.Version == Sitecore.Data.Version.First)
            {
                if (Settings.ItemCloning.ForceUpdate && !allClones.Any<Item>() && latestVersion.Versions.GetVersions(true).Length == 1)
                    NotificationManager.HandleChildAddedEvent(latestVersion.Parent, latestVersion.ID, true);
                Item sharedFieldsSource = latestVersion.SharedFieldsSource;
                if (sharedFieldsSource != null)
                    sharedFieldsSource = ItemManager.GetItem(sharedFieldsSource.ID, latestVersion.Language, Sitecore.Data.Version.Latest, sharedFieldsSource.Database, SecurityCheck.Disable);
                if (sharedFieldsSource != null)
                {
                    latestVersion.Editing.BeginEdit();
                    latestVersion[FieldIDs.Source] = sharedFieldsSource.Uri.ToString();
                    latestVersion.Editing.EndEdit();
                }
                foreach (Item obj in allClones)
                {
                    if (obj.SourceUri == latestVersion.Uri)
                        set.Add(obj.ID);
                }
            }
            foreach (Item obj in allClones)
            {
                if (!set.Contains(obj.ID))
                {
                    Assert.IsNotNull((object)obj.Database.NotificationProvider, "NotificationProvider");
                    if (latestVersion.Version == Sitecore.Data.Version.First)
                    {
                        set.Add(obj.ID);
                        FirstVersionAddedNotification addedNotification = new FirstVersionAddedNotification();
                        addedNotification.Uri = new ItemUri(obj.ID, obj.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, obj.Database.Name);
                        addedNotification.VersionUri = latestVersion.Uri;
                        Notification notification = (Notification)addedNotification;
                        if (Settings.ItemCloning.ForceUpdate)
                        {
                            ((VersionAddedNotification)notification).ForceAccept = true;
                            notification.Accept(obj);
                        }
                        else
                            obj.Database.NotificationProvider.AddNotification(notification);
                    }
                    else
                    {
                        Language language = obj.Language;
                        if (!(language != latestVersion.Language))
                        {
                            set.Add(obj.ID);
                            VersionAddedNotification addedNotification = new VersionAddedNotification();
                            addedNotification.Uri = new ItemUri(obj.ID, language, Sitecore.Data.Version.Latest, obj.Database);
                            addedNotification.VersionUri = latestVersion.Uri;
                            Notification notification = (Notification)addedNotification;
                            //if (Settings.ItemCloning.ForceUpdate)
                            //{
                            //    ((VersionAddedNotification)notification).ForceAccept = true;
                            //    notification.Accept(obj);
                            //}
                            //else
                            obj.Database.NotificationProvider.AddNotification(notification);
                        }
                    }
                }
            }
        }

        /// <summary>The create item event.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        private static void DataEngine_CreatedItem(object sender, ExecutedEventArgs<CreateItemCommand> e)
        {
            Assert.ArgumentNotNull((object)e, "e");
            if (Settings.ItemCloning.ForceUpdate)
                return;
            NotificationManager.HandleChildAddedEvent(e.Command.Destination, e.Command.ItemId, false);
        }

        /// <summary>Handles the child added event.</summary>
        /// <param name="parent">The parent.</param>
        /// <param name="childId">The created child Id.</param>
        /// <param name="forceChildCloneCreation">Specifies whether the child clone creation should be forced.</param>
        private static void HandleChildAddedEvent(Item parent, ID childId, bool forceChildCloneCreation = false)
        {
            Assert.ArgumentNotNull((object)parent, "parent");
            Assert.ArgumentNotNull((object)childId, "childId");
            IEnumerable<Item> allClones = NotificationManager.GetAllClones(parent);
            List<ID> idList = new List<ID>();
            foreach (Item obj in allClones)
            {
                if (!idList.Contains(obj.ID))
                {
                    idList.Add(obj.ID);
                    ChildCreatedNotification createdNotification1 = new ChildCreatedNotification();
                    createdNotification1.Uri = new ItemUri(obj.ID, obj.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, obj.Database.Name);
                    createdNotification1.ChildId = childId;
                    ChildCreatedNotification createdNotification2 = createdNotification1;
                    if (forceChildCloneCreation)
                    {
                        createdNotification2.Accept(obj);
                    }
                    else
                    {
                        Assert.IsNotNull((object)obj.Database.NotificationProvider, "NotificationProvider");
                        obj.Database.NotificationProvider.AddNotification((Notification)createdNotification2);
                    }
                }
            }
        }

        /// <summary>Handles the item moved event.</summary>
        /// <param name="parent">The parent.</param>
        /// <param name="childItem">The child item.</param>
        /// <param name="oldParentId">The ID of old parent</param>
        private static void HandleItemMovedEvent(Item parent, Item childItem, ID oldParentId)
        {
            Assert.ArgumentNotNull((object)parent, "parent");
            Assert.ArgumentNotNull((object)childItem, "childItem");
            IEnumerable<Item> list1 = (IEnumerable<Item>)NotificationManager.GetAllClones(parent).ToList<Item>();
            Assert.IsNotNull((object)childItem, "Child item cannot be null");
            IEnumerable<Item> list2 = (IEnumerable<Item>)NotificationManager.GetAllClones(childItem).ToList<Item>();
            if (!list1.Any<Item>() && list2.Any<Item>())
            {
                foreach (Item obj in list2)
                {
                    ID initialParentId = oldParentId;
                    if (NotificationManager.CheckIfItemMovedToInitialParent(parent, obj, ref initialParentId))
                        break;
                    NotificationManager.RemoveExistingItemMovedNotifications(obj);
                    NotificationManager.AddItemMovedNotification(obj, parent, childItem, true, initialParentId);
                    NotificationManager.AddItemMovedChildRemovedNotification(obj, childItem, true);
                }
            }
            else
            {
                List<ID> idList = new List<ID>();
                foreach (Item obj1 in list1)
                {
                    if (!idList.Contains(obj1.ID))
                    {
                        Assert.IsNotNull((object)obj1.Database.NotificationProvider, "NotificationProvider");
                        Item obj2 = NotificationManager.ResolveChildClone(obj1, list2);
                        if (obj2 != null)
                        {
                            ID initialParentId = oldParentId;
                            if (NotificationManager.CheckIfItemMovedToInitialParent(parent, obj2, ref initialParentId))
                                break;
                            NotificationManager.RemoveExistingItemMovedNotifications(obj2);
                            NotificationManager.AddItemMovedChildRemovedNotification(obj2, childItem, false);
                            NotificationManager.AddItemMovedNotification(obj2, obj1, childItem, false, initialParentId);
                            NotificationManager.AddItemMovedChildCreatedNotification(obj1, obj2);
                            idList.Add(obj1.ID);
                        }
                    }
                }
            }
        }

        /// <summary>Checks if item moved to initial parent.</summary>
        /// <param name="parent">The parent.</param>
        /// <param name="childClone">The child clone.</param>
        /// <param name="initialParentId">The initial parent id.</param>
        /// <returns></returns>
        private static bool CheckIfItemMovedToInitialParent(Item parent, Item childClone, ref ID initialParentId)
        {
            Assert.ArgumentNotNull((object)parent, "parent");
            Assert.ArgumentNotNull((object)childClone, "childClone");
            ItemMovedNotification movedNotification = NotificationManager.GetItemMovedNotification(childClone);
            if (movedNotification != null)
            {
                if (movedNotification.InitialParentID == parent.ID)
                {
                    NotificationManager.RemoveExistingItemMovedNotifications(childClone);
                    return true;
                }
                ID initialParentId1 = movedNotification.InitialParentID;
                if (initialParentId1 != (ID)null && ID.Null != initialParentId1)
                    initialParentId = movedNotification.InitialParentID;
            }
            return false;
        }

        /// <summary>Called when the add is item moved child created.</summary>
        /// <param name="parentClone">The parent clone.</param>
        /// <param name="cloneItem">The clone item.</param>
        internal static void AddItemMovedChildCreatedNotification(Item parentClone, Item cloneItem)
        {
            Assert.ArgumentNotNull((object)parentClone, "parentClone");
            Assert.ArgumentNotNull((object)cloneItem, "cloneItem");
            if (parentClone.Database.NotificationProvider == null)
                return;
            ItemMovedChildCreatedNotification createdNotification1 = new ItemMovedChildCreatedNotification();
            createdNotification1.Uri = new ItemUri(parentClone.ID, parentClone.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, parentClone.Database.Name);
            createdNotification1.ChildId = new ItemUri(cloneItem.ID, cloneItem.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, cloneItem.Database.Name);
            ItemMovedChildCreatedNotification createdNotification2 = createdNotification1;
            parentClone.Database.NotificationProvider.AddNotification((Notification)createdNotification2);
        }

        /// <summary>Called when the add is item moved child removed.</summary>
        /// <param name="childClone">The child clone.</param>
        /// <param name="originalItem">The original item.</param>
        /// <param name="movedOutCloneTree">if set to <c>true</c> [moved out clone tree].</param>
        internal static void AddItemMovedChildRemovedNotification(Item childClone, Item originalItem, bool movedOutCloneTree)
        {
            Assert.ArgumentNotNull((object)childClone, "childClone");
            Assert.ArgumentNotNull((object)originalItem, "originalItem");
            if (childClone.Database.NotificationProvider == null || childClone.Parent == null || !childClone.Parent.IsClone)
                return;
            ItemMovedChildRemovedNotification removedNotification1 = new ItemMovedChildRemovedNotification();
            removedNotification1.Uri = new ItemUri(childClone.Parent.ID, childClone.Parent.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, childClone.Parent.Database.Name);
            removedNotification1.ChildId = new ItemUri(childClone.ID, childClone.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, childClone.Database.Name);
            removedNotification1.OriginalItemId = new ItemUri(originalItem.ID, originalItem.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, originalItem.Database.Name);
            removedNotification1.MovedOutCloneTree = movedOutCloneTree;
            ItemMovedChildRemovedNotification removedNotification2 = removedNotification1;
            childClone.Database.NotificationProvider.AddNotification((Notification)removedNotification2);
        }

        /// <summary>Called when the add is item moved.</summary>
        /// <param name="cloneItem">The clone item.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="originalItem">The original item.</param>
        /// <param name="movedOutCloneTree">if set to <c>true</c> [moved out clone tree].</param>
        /// <param name="oldParentId">The old parent id.</param>
        internal static void AddItemMovedNotification(Item cloneItem, Item parent, Item originalItem, bool movedOutCloneTree, ID oldParentId)
        {
            Assert.ArgumentNotNull((object)cloneItem, "cloneItem");
            Assert.ArgumentNotNull((object)parent, "parent");
            Assert.ArgumentNotNull((object)originalItem, "originalItem");
            if (cloneItem.Database.NotificationProvider == null)
                return;
            ItemMovedNotification movedNotification1 = new ItemMovedNotification();
            movedNotification1.Uri = new ItemUri(cloneItem.ID, cloneItem.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, cloneItem.Database.Name);
            movedNotification1.ParentId = new ItemUri(parent.ID, parent.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, parent.Database.Name);
            movedNotification1.OriginalItemId = new ItemUri(originalItem.ID, originalItem.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, originalItem.Database.Name);
            movedNotification1.MovedOutCloneTree = movedOutCloneTree;
            movedNotification1.InitialParentID = oldParentId;
            ItemMovedNotification movedNotification2 = movedNotification1;
            cloneItem.Database.NotificationProvider.AddNotification((Notification)movedNotification2);
        }

        /// <summary>Removes the existing notifications.</summary>
        /// <param name="clone">The clone.</param>
        internal static void RemoveExistingItemMovedNotifications(Item clone)
        {
            Assert.ArgumentNotNull((object)clone, "clone");
            if (clone.Database.NotificationProvider == null)
                return;
            foreach (Notification notification1 in clone.Database.NotificationProvider.GetNotifications(clone).Where<Notification>((Func<Notification, bool>)(n => n is ItemMovedNotification)))
            {
                ItemMovedNotification movedNotification = notification1 as ItemMovedNotification;
                if (movedNotification == null)
                    break;
                foreach (Notification notification2 in clone.Database.NotificationProvider.GetNotifications(movedNotification.ParentId).Where<Notification>((Func<Notification, bool>)(n =>
                {
                    if (!(n is ItemMovedChildCreatedNotification))
                        return n is ItemMovedChildRemovedNotification;
                    return true;
                })))
                {
                    ItemMovedChildCreatedNotification createdNotification = notification2 as ItemMovedChildCreatedNotification;
                    if (createdNotification != null)
                        clone.Database.NotificationProvider.RemoveNotification(createdNotification.ID);
                }
                Item parent = clone.Parent;
                if (parent != null)
                {
                    foreach (Notification notification2 in clone.Database.NotificationProvider.GetNotifications(parent))
                    {
                        ItemMovedChildRemovedNotification removedNotification = notification2 as ItemMovedChildRemovedNotification;
                        if (removedNotification != null)
                            clone.Database.NotificationProvider.RemoveNotification(removedNotification.ID);
                    }
                }
                clone.Database.NotificationProvider.RemoveNotification(notification1.ID);
            }
        }

        /// <summary>Resolves the child clone.</summary>
        /// <param name="parent">The parent.</param>
        /// <param name="childClones">The child clones.</param>
        /// <returns>The child clone.</returns>
        private static Item ResolveChildClone(Item parent, IEnumerable<Item> childClones)
        {
            Assert.ArgumentNotNull((object)parent, "parent");
            Assert.ArgumentNotNull((object)childClones, "childClones");
            string[] strArray1 = parent.Paths.LongID.Split('/');
            Dictionary<Item, int> matchedItems = new Dictionary<Item, int>();
            using (IEnumerator<Item> enumerator = childClones.GetEnumerator())
            {
                label_5:
                while (enumerator.MoveNext())
                {
                    Item current = enumerator.Current;
                    string[] strArray2 = current.Paths.LongID.Split('/');
                    int num1 = strArray1.Length > strArray2.Length ? strArray2.Length : strArray1.Length;
                    int num2 = 0;
                    int index = 0;
                    while (true)
                    {
                        if (index < num1 && !(strArray1[index] != strArray2[index]))
                        {
                            matchedItems[current] = ++num2;
                            ++index;
                        }
                        else
                            goto label_5;
                    }
                }
            }
            return matchedItems.Where<KeyValuePair<Item, int>>((Func<KeyValuePair<Item, int>, bool>)(item => item.Value == matchedItems.Max<KeyValuePair<Item, int>>((Func<KeyValuePair<Item, int>, int>)(p => p.Value)))).Select<KeyValuePair<Item, int>, Item>((Func<KeyValuePair<Item, int>, Item>)(item => item.Key)).FirstOrDefault<Item>();
        }

        /// <summary>Handles the child removed.</summary>
        /// <param name="parent">The parent.</param>
        /// <param name="childId">The child id.</param>
        private static void HandleChildRemoved(Item parent, ID childId)
        {
            Assert.ArgumentNotNull((object)parent, "parent");
            Assert.ArgumentNotNull((object)childId, "childId");
            IEnumerable<Item> allClones = NotificationManager.GetAllClones(parent);
            List<ID> idList = new List<ID>();
            foreach (Item clone in allClones)
            {
                if (!idList.Contains(clone.ID) && clone.Database.NotificationProvider != null)
                {
                    foreach (Notification notification in new List<Notification>(clone.Database.NotificationProvider.GetNotifications(clone)))
                    {
                        ChildCreatedNotification createdNotification = notification as ChildCreatedNotification;
                        if (createdNotification != null && createdNotification.ChildId == childId && !createdNotification.Processed)
                            clone.Database.NotificationProvider.RemoveNotification(createdNotification.ID);
                    }
                }
            }
        }

        /// <summary>
        /// Handles the SavedItem event of the DataEngine control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event args.</param>
        private static void DataEngine_SavedItem(object sender, ExecutedEventArgs<SaveItemCommand> e)
        {
            Assert.ArgumentNotNull((object)e, "e");
            ID[] inheritedFieldIds = CloneItem.GetNonInheritedFieldIDs();
            string[] inheritedFieldKeys = CloneItem.GetNonInheritedFieldKeys();
            Func<FieldChange, string, bool> checkNonInheritedFieldKey = (Func<FieldChange, string, bool>)((fieldChange, key) =>
            {
                ID result;
                if (ID.TryParse(key, out result))
                    return fieldChange.FieldID == result;
                if (fieldChange.Definition != null)
                    return fieldChange.Definition.Key == key;
                return false;
            });
            Item obj = e.Command.Item;
            IEnumerable<Item> allClones = NotificationManager.GetAllClones(obj);
            Dictionary<ID, List<ID>> dictionary1 = new Dictionary<ID, List<ID>>();
            Dictionary<string, List<ID>> dictionary2 = new Dictionary<string, List<ID>>();
            foreach (Item clone in allClones)
            {
                if (clone.Database.NotificationProvider != null)
                {
                    IEnumerable<Notification> notifications = clone.Database.NotificationProvider.GetNotifications(clone);
                    foreach (FieldChange fieldChange in e.Command.Changes.FieldChanges)
                    {
                        FieldChange dummy = fieldChange;
                        if (!((IEnumerable<ID>)inheritedFieldIds).Any<ID>((Func<ID, bool>)(id => id == dummy.FieldID)) && !((IEnumerable<string>)inheritedFieldKeys).Any<string>((Func<string, bool>)(key => checkNonInheritedFieldKey(dummy, key))) && fieldChange.Definition != null && (!fieldChange.Definition.IsVersioned || !(clone.SourceUri != obj.Uri)))
                        {
                            if (fieldChange.Definition.IsShared)
                            {
                                if (!dictionary1.ContainsKey(fieldChange.FieldID))
                                    dictionary1.Add(fieldChange.FieldID, new List<ID>());
                                if (!dictionary1[fieldChange.FieldID].Contains(clone.ID))
                                    dictionary1[fieldChange.FieldID].Add(clone.ID);
                                else
                                    continue;
                            }
                            else if (fieldChange.Definition.IsUnversioned)
                            {
                                if (!(clone.Language != obj.Language))
                                {
                                    string key = string.Format("{0}_{1}", (object)fieldChange.FieldID.ToString().ToLowerInvariant(), (object)obj.Language.Name);
                                    if (!dictionary2.ContainsKey(key))
                                        dictionary2.Add(key, new List<ID>());
                                    if (!dictionary2[key].Contains(clone.ID))
                                        dictionary2[key].Add(clone.ID);
                                    else
                                        continue;
                                }
                                else
                                    continue;
                            }
                            if (clone.Fields[fieldChange.FieldID].HasValue)
                                NotificationManager.RegisterFieldChangedNotification(fieldChange, clone, obj, notifications);
                        }
                    }
                }
            }
        }

        /// <summary>Called when the register is field changed.</summary>
        /// <param name="change">The change.</param>
        /// <param name="clone">The clone.</param>
        /// <param name="originalItem">The original item.</param>
        /// <param name="notifications">The notifications.</param>
        private static void RegisterFieldChangedNotification(FieldChange change, Item clone, Item originalItem, IEnumerable<Notification> notifications)
        {
            Assert.ArgumentNotNull((object)change, "change");
            Assert.ArgumentNotNull((object)clone, "clone");
            Assert.ArgumentNotNull((object)originalItem, "originalItem");
            Assert.ArgumentNotNull((object)notifications, "notifications");
            if (change.Definition == null || clone.Database.NotificationProvider == null)
                return;
            NotificationManager.RemoveDuplicatedFieldChangedNotification(change, clone, notifications);
            ItemUri itemUri = !change.Definition.IsVersioned ? (!change.Definition.IsShared ? new ItemUri(clone.ID, clone.Language, Sitecore.Data.Version.Latest, clone.Database) : new ItemUri(clone.ID, clone.ID.ToString(), Language.Invariant, Sitecore.Data.Version.Latest, clone.Database.Name)) : clone.Uri;
            NotificationProvider notificationProvider = clone.Database.NotificationProvider;
            FieldChangedNotification changedNotification1 = new FieldChangedNotification();
            changedNotification1.Uri = itemUri;
            changedNotification1.FieldID = change.FieldID;
            changedNotification1.OldValue = originalItem[change.FieldID];
            changedNotification1.NewValue = change.Value;
            changedNotification1.VersionUri = new ItemUri(originalItem);
            FieldChangedNotification changedNotification2 = changedNotification1;
            notificationProvider.AddNotification((Notification)changedNotification2);
        }

        /// <summary>Called when the remove is duplicated field changed.</summary>
        /// <param name="change">The change.</param>
        /// <param name="clone">The clone.</param>
        /// <param name="notifications">The notifications.</param>
        private static void RemoveDuplicatedFieldChangedNotification(FieldChange change, Item clone, IEnumerable<Notification> notifications)
        {
            Assert.ArgumentNotNull((object)change, "change");
            Assert.ArgumentNotNull((object)clone, "clone");
            Assert.ArgumentNotNull((object)notifications, "notifications");
            if (clone.Database.NotificationProvider == null)
                return;
            foreach (Notification notification in notifications)
            {
                FieldChangedNotification changedNotification = notification as FieldChangedNotification;
                if (changedNotification != null && !(changedNotification.FieldID != change.FieldID))
                {
                    clone.Database.NotificationProvider.RemoveNotification(changedNotification.ID);
                    break;
                }
            }
        }

        /// <summary>The get all clones.</summary>
        /// <param name="source">The source.</param>
        /// <returns>The list of clones.</returns>
        private static IEnumerable<Item> GetAllClones(Item source)
        {
            Assert.ArgumentNotNull((object)source, "source");
            ItemLink[] referrers = Globals.LinkDatabase.GetReferrers(source, FieldIDs.Source);
            ItemLink[] itemLinkArray = referrers.Length > 0 ? new ItemLink[0] : Globals.LinkDatabase.GetReferrers(source, FieldIDs.SourceItem);
            return ((IEnumerable<ItemLink>)((IEnumerable<ItemLink>)referrers).Concat<ItemLink>((IEnumerable<ItemLink>)itemLinkArray).ToArray<ItemLink>()).Select<ItemLink, Item>((Func<ItemLink, Item>)(clone => clone.GetSourceItem())).Where<Item>((Func<Item, bool>)(clone =>
            {
                if (clone != null && clone.SourceUri != (ItemUri)null)
                    return clone.SourceUri.ItemID == source.ID;
                return false;
            }));
        }

        /// <summary>The get clones of version.</summary>
        /// <param name="source">The source.</param>
        /// <returns>The list of item version clones.</returns>
        private static IEnumerable<Item> GetClonesOfVersion(Item source)
        {
            Assert.ArgumentNotNull((object)source, "source");
            return NotificationManager.GetAllClones(source).Where<Item>((Func<Item, bool>)(clone => clone.SourceUri == source.Uri));
        }

        /// <summary>Hanldes changing template.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="T:System.EventArgs" /> instance containing the event data.</param>
        private static void TemplateChangedHandler(object sender, EventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull((object)e, "e");
            Item parameter1 = Event.ExtractParameter(e, 0) as Item;
            TemplateChangeList parameter2 = Event.ExtractParameter(e, 1) as TemplateChangeList;
            if (parameter1 == null || parameter2 == null || !parameter1.HasClones)
                return;
            ItemUri itemUri = new ItemUri(parameter1.ID, parameter1.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, parameter1.Database.Name);
            HashSet<string> stringSet = new HashSet<string>();
            foreach (Item clone in parameter1.GetClones())
            {
                if (clone.Database.NotificationProvider != null && stringSet.Add(clone.ID.ToString() + clone.Database.Name))
                {
                    ItemUri cloneUri = new ItemUri(clone.ID, clone.Paths.FullPath, Language.Invariant, Sitecore.Data.Version.Latest, clone.Database.Name);
                    foreach (Notification notification in clone.Database.NotificationProvider.GetNotifications().Where<Notification>((Func<Notification, bool>)(n =>
                    {
                        if (!(n is OriginalItemChangedTemplateNotification))
                            return false;
                        OriginalItemChangedTemplateNotification templateNotification = (OriginalItemChangedTemplateNotification)n;
                        if (templateNotification.Uri.ItemID == cloneUri.ItemID)
                            return templateNotification.Uri.DatabaseName == cloneUri.DatabaseName;
                        return false;
                    })))
                        clone.Database.NotificationProvider.RemoveNotification(notification.ID);
                    NotificationProvider notificationProvider = clone.Database.NotificationProvider;
                    OriginalItemChangedTemplateNotification templateNotification1 = new OriginalItemChangedTemplateNotification();
                    templateNotification1.OriginalItemUri = itemUri;
                    templateNotification1.NewTemplateID = parameter2.Target.ID;
                    templateNotification1.OldTemplateID = parameter2.Source.ID;
                    templateNotification1.Uri = cloneUri;
                    OriginalItemChangedTemplateNotification templateNotification2 = templateNotification1;
                    notificationProvider.AddNotification((Notification)templateNotification2);
                }
            }
        }

        /// <summary>Gets the ItemMovedNotification by item clone.</summary>
        /// <param name="item">The item.</param>
        /// <returns></returns>
        private static ItemMovedNotification GetItemMovedNotification(Item item)
        {
            Assert.ArgumentNotNull((object)item, "item");
            NotificationProvider notificationProvider = item.Database.NotificationProvider;
            if (notificationProvider != null)
                return notificationProvider.GetNotifications(item).Where<Notification>((Func<Notification, bool>)(n => n is ItemMovedNotification)).FirstOrDefault<Notification>() as ItemMovedNotification;
            return (ItemMovedNotification)null;
        }

        /// <summary>Removes the "child removed" notifications.</summary>
        /// <param name="notificationProvider">The notification provider.</param>
        /// <param name="child">The child.</param>
        internal static void RemoveChildRemovedNotifications(NotificationProvider notificationProvider, Item child)
        {
            Assert.ArgumentNotNull((object)notificationProvider, "notificationProvider");
            Assert.ArgumentNotNull((object)child, "child");
            foreach (Notification notification in notificationProvider.GetNotifications())
            {
                ItemMovedChildRemovedNotification removedNotification = notification as ItemMovedChildRemovedNotification;
                if (removedNotification != null && removedNotification.ChildId.ItemID == child.ID && removedNotification.ChildId.DatabaseName == child.Database.Name)
                    notificationProvider.RemoveNotification(removedNotification.ID);
            }
        }
    }
}

namespace Sitecore.Support.Pipelines.InitializeManagers
{
    using Sitecore.Pipelines;
    using Support.Data.Managers;

    [UsedImplicitly]
    public class InitializeNotificationManager
    {
        /// <summary>Processsor entry point method.</summary>
        /// <param name="args"></param>
        [UsedImplicitly]
        public void Process(PipelineArgs args)
        {
            NotificationManager.Initialize();
        }
    }
}

namespace Sitecore.Support.Pipelines.Loader
{
    using System;
    using Diagnostics;
    using Eventing;
    using Events;
    using Publishing;
    using Sitecore.Data.Managers;
    using Sitecore.Data.Proxies;
    using Sitecore.Data.Serialization;
    using Sitecore.Pipelines;
    using Sitecore.Search;

    public class InitializeManagers
    {
        /// <summary>The pipeline name</summary>
        public const string PipelineName = "initializeManagers";

        /// <summary>Initializes static manager classes.</summary>
        /// <param name="args">The arguments.</param>
        /// <exception cref="T:Sitecore.Exceptions.ConfigurationException"></exception>
        public void Process(PipelineArgs args)
        {
            CorePipeline pipeline = CorePipelineFactory.GetPipeline("initializeManagers", string.Empty);
            if (pipeline != null)
            {
                pipeline.Run(args);
            }
            else
            {
                Log.SingleError(string.Format("The '{0}' pipeline is not defined in the Sitecore configuration file.", (object)"initializeManagers"), (object)typeof(InitializeManagers));
                this.InitManagersInternally();
            }
        }

        [Obsolete("This method is fallback functionality for CMS 8.1 upgrade procedure. It will be removed in latest version.")]
        private void InitManagersInternally()
        {
            Event.Initialize();
            ItemManager.Initialize();
            ProxyManager.Initialize();
            HistoryManager.Initialize();
            IndexingManager.Initialize();
            LanguageManager.Initialize();
            PublishManager.Initialize();
            SearchManager.Initialize();
            Manager.Initialize();
            Support.Data.Managers.NotificationManager.Initialize();
            EventManager.Initialize();
        }
    }
}

namespace Sitecore.Support.Data.DataProviders.SqlServer
{
    using System;
    using System.Collections.Generic;
    using Common;
    using Diagnostics;
    using Globalization;
    using Sitecore.Data;
    using Sitecore.Data.Clones;
    using Sitecore.Data.DataProviders.Sql;
    using Sitecore.Data.SqlServer;
    using Sitecore.Data.Items;


    public class SqlServerNotificationProvider : NotificationProvider
    {
        /// <summary>The database connection string.</summary>
        private SqlDataApi dataApi;
        /// <summary>The name of database provider refers to.</summary>
        private string databaseName;

        /// <summary>Gets the data API.</summary>
        /// <value>The data API.</value>
        protected SqlDataApi DataApi
        {
            get
            {
                return this.dataApi;
            }
        }

        /// <summary>Gets the name of the database the provider refers to.</summary>
        /// <value>The name of the database.</value>
        protected string DatabaseName
        {
            get
            {
                return this.databaseName;
            }
        }

        /// <summary>Gets or sets the serializer.</summary>
        /// <value>The serializer.</value>
        protected Serializer Serializer { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Sitecore.Data.DataProviders.SqlServer.SqlServerNotificationProvider" /> class.
        /// </summary>
        /// <param name="connectionStringName">The connection string name.</param>
        /// <param name="databaseName">Name of the database.</param>
        public SqlServerNotificationProvider(string connectionStringName, string databaseName)
          : this((SqlDataApi)new SqlServerDataApi(connectionStringName), databaseName)
        {
            Assert.ArgumentNotNull((object)connectionStringName, "connectionStringName");
            Assert.ArgumentNotNull((object)databaseName, "databaseName");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Sitecore.Data.DataProviders.SqlServer.SqlServerNotificationProvider" /> class.
        /// </summary>
        /// <param name="api">The Data API.</param>
        /// <param name="databaseName">Name of the database.</param>
        protected SqlServerNotificationProvider(SqlDataApi api, string databaseName)
        {
            Assert.ArgumentNotNullOrEmpty(databaseName, "databaseName");
            Assert.ArgumentNotNull((object)api, "api");
            this.dataApi = api;
            this.Serializer = new Serializer();
            this.databaseName = databaseName;
        }

        /// <summary>The add notification.</summary>
        /// <param name="notification">The notification.</param>
        /// <exception cref="T:System.NotImplementedException">
        /// </exception>
        public override void AddNotification(Notification notification)
        {
            Assert.ArgumentNotNull((object)notification, "notification");
            Assert.IsNotNull((object)notification.Uri, "uri");
            Assert.IsNotNull((object)notification.Uri.ItemID, "item ID");
            Assert.IsNotNull((object)notification.Uri.Language, "language");
            Assert.IsNotNull((object)notification.Uri.Version, "version");
            string sql = "INSERT INTO {0}Notifications{1} (\r\n                      {0}Id{1}, {0}ItemId{1}, {0}Language{1}, {0}Version{1}, {0}Processed{1}, {0}InstanceType{1}, {0}InstanceData{1}, {0}Created{1}\r\n                    )\r\n                    VALUES (\r\n                      {2}id{3}, {2}itemId{3}, {2}language{3}, {2}version{3}, {2}processed{3}, {2}instanceType{3}, {2}instanceData{3}, {2}created{3}\r\n                    )";
            string assemblyQualifiedName = notification.GetType().AssemblyQualifiedName;
            string str = this.Serializer.Serialize<Notification>(notification);
            object[] objArray = new object[16]
            {
        (object) "id",
        (object) notification.ID,
        (object) "itemId",
        (object) notification.Uri.ItemID,
        (object) "language",
        (object) notification.Uri.Language.Name,
        (object) "version",
        (object) notification.Uri.Version,
        (object) "processed",
        (object) notification.Processed,
        (object) "instanceType",
        (object) assemblyQualifiedName,
        (object) "instanceData",
        (object) str,
        (object) "created",
        (object) DateTime.UtcNow
            };
            this.DataApi.Execute(sql, objArray);
        }

        /// <summary>The get notification.</summary>
        /// <param name="id">The notification id.</param>
        /// <returns>The Notification object by Id.</returns>
        public override Notification GetNotification(ID id)
        {
            Assert.ArgumentNotNull((object)id, "id");
            List<Notification> notificationList = new List<Notification>(this.DataApi.CreateObjectReader<Notification>("SELECT \r\n          {0}Id{1}, {0}ItemId{1}, {0}Language{1}, {0}Version{1}, {0}Processed{1}, {0}InstanceType{1}, {0}InstanceData{1}, {0}Created{1} \r\n        FROM {0}Notifications{1} WITH (NOLOCK)\r\n        WHERE {0}Id{1}={2}id{3}", new object[2]
            {
        (object) "id",
        (object) id
            }, new Func<DataProviderReader, Notification>(this.CreateNotification)));
            if (notificationList.Count == 0 || notificationList.Count > 1)
                return (Notification)null;
            return notificationList[0];
        }

        /// <summary>The get notifications.</summary>
        /// <param name="clone">The Stecore item.</param>
        /// <returns>The notifications for the specified item.</returns>
        public override IEnumerable<Notification> GetNotifications(Item clone)
        {
            Assert.ArgumentNotNull((object)clone, "clone");
            return this.GetNotifications(clone.Uri);
        }

        /// <summary>Gets the notifications.</summary>
        /// <param name="itemUri">The item URI.</param>
        /// <returns>The notifications.</returns>
        public override IEnumerable<Notification> GetNotifications(ItemUri itemUri)
        {
            Assert.ArgumentNotNull((object)itemUri, "itemUri");

            IEnumerable<Notification> notifications = this.DataApi.CreateObjectReader<Notification>("SELECT \r\n          {0}Id{1}, {0}ItemId{1}, {0}Language{1}, {0}Version{1}, {0}Processed{1}, {0}InstanceType{1}, {0}InstanceData{1}, {0}Created{1}  \r\n        FROM {0}Notifications{1} WITH (NOLOCK)\r\n        WHERE {0}Processed{1}={2}processed{3} AND {0}ItemId{1}={2}itemId{3}\r\n              AND (({0}Language{1}={2}invariantLanguage{3}) OR ({0}Language{1}={2}language{3}) OR ({0}Language{1} IS NULL))\r\n              AND (({0}Version{1}={2}latestVersion{3}) OR ({0}Version{1}={2}version{3}))\r\n        ORDER BY {0}Created{1}", new object[12]
            {
        (object) "processed",
        (object) false,
        (object) "itemId",
        (object) itemUri.ItemID,
        (object) "invariantLanguage",
        (object) Language.Invariant.ToString(),
        (object) "language",
        (object) itemUri.Language.ToString(),
        (object) "latestVersion",
        (object) Sitecore.Data.Version.Latest.Number,
        (object) "version",
        (object) itemUri.Version.Number
            }, new Func<DataProviderReader, Notification>(this.CreateNotification));


            if (Configuration.Settings.ItemCloning.ForceUpdate)
            {
                Item i = Sitecore.Data.Database.GetDatabase(itemUri.DatabaseName).GetItem(itemUri.ItemID);

                foreach (var notification in notifications)
                {
                    VersionAddedNotification n = notification as VersionAddedNotification;

                    if (n != null)
                    {
                        n.ForceAccept = true;
                    }

                    notification.Accept(i);
                }
            }
            return notifications;
        }

        /// <summary>Gets the notifications.</summary>
        /// <param name="notificationType">Type of the notification.</param>
        /// <returns>The notifications.</returns>
        public override IEnumerable<Notification> GetNotifications(Type notificationType)
        {
            Assert.ArgumentNotNull((object)notificationType, "notificationType");
            return this.DataApi.CreateObjectReader<Notification>("SELECT \r\n          {0}Id{1}, {0}ItemId{1}, {0}Language{1}, {0}Version{1}, {0}Processed{1}, {0}InstanceType{1}, {0}InstanceData{1}, {0}Created{1}  \r\n        FROM {0}Notifications{1} WITH (NOLOCK)\r\n        WHERE {0}Processed{1}={2}processed{3} AND {0}InstanceType{1}={2}instanceType{3}              \r\n        ORDER BY {0}Created{1}", new object[4]
            {
        (object) "processed",
        (object) false,
        (object) "instanceType",
        (object) notificationType.AssemblyQualifiedName
            }, new Func<DataProviderReader, Notification>(this.CreateNotification));
        }

        /// <summary>Gets all notifications.</summary>
        /// <returns>The list of all registered Notifications.</returns>
        public override IEnumerable<Notification> GetNotifications()
        {
            return this.DataApi.CreateObjectReader<Notification>("SELECT \r\n          {0}Id{1}, {0}ItemId{1}, {0}Language{1}, {0}Version{1}, {0}Processed{1}, {0}InstanceType{1}, {0}InstanceData{1}, {0}Created{1} \r\n        FROM {0}Notifications{1} WITH (NOLOCK) ORDER BY {0}Created{1}", new object[0], new Func<DataProviderReader, Notification>(this.CreateNotification));
        }

        /// <summary>The remove notification.</summary>
        /// <param name="id">The notification id.</param>
        /// <returns>
        /// <c>true</c> if notification with specified id has been removed.
        /// </returns>
        public override bool RemoveNotification(ID id)
        {
            Assert.ArgumentNotNull((object)id, "id");
            return this.DataApi.Execute("DELETE FROM {0}Notifications{1} WHERE {0}Id{1}={2}id{3}", (object)"id", (object)id) == 1;
        }

        /// <summary>Removes obsolete notifications.</summary>
        public override void Cleanup()
        {
            foreach (Notification notification in this.DataApi.CreateObjectReader<Notification>("SELECT \r\n          {0}n{1}.{0}Id{1}, {0}n{1}.{0}ItemId{1}, {0}n{1}.{0}Language{1}, {0}n{1}.{0}Version{1}, {0}n{1}.{0}Processed{1}, {0}n{1}.{0}InstanceType{1}, {0}n{1}.{0}InstanceData{1} \r\n        FROM {0}Notifications{1} as {0}n{1}  WITH (NOLOCK)\r\n          WHERE NOT EXISTS(SELECT * FROM {0}Items{1} as {0}i{1}\r\n                            WHERE {0}i{1}.{0}Id{1}={0}n{1}.{0}ItemId{1} AND\r\n                                  (({0}n{1}.{0}Version{1}=0) OR EXISTS(SELECT * FROM {0}VersionedFields{1} as {0}v{1}\r\n                                                                        WHERE ({0}v{1}.{0}ItemId{1}={0}n{1}.{0}ItemId{1}) AND \r\n                                                                              ({0}v{1}.{0}Language{1}={0}n{1}.{0}Language{1}) AND\r\n                                                                              ({0}v{1}.{0}Version{1}={0}n{1}.{0}Version{1}))))", new object[0], new Func<DataProviderReader, Notification>(this.CreateNotification)))
                this.RemoveNotification(notification.ID);
            base.Cleanup();
        }

        /// <summary>The create the notification from table row.</summary>
        /// <param name="reader">The data reader.</param>
        /// <returns>The saved Notification object.</returns>
        protected Notification CreateNotification(DataProviderReader reader)
        {
            Assert.ArgumentNotNull((object)reader, "reader");
            ID id = new ID(this.DataApi.GetGuid(0, reader));
            ID itemID = new ID(this.DataApi.GetGuid(1, reader));
            Language language = Language.Parse(this.DataApi.GetString(2, reader));
            Sitecore.Data.Version version = Sitecore.Data.Version.Parse(this.DataApi.GetInt(3, reader));
            bool boolean = this.DataApi.GetBoolean(4, reader);
            Type type = Type.GetType(this.DataApi.GetString(5, reader));
            Notification notification = this.Serializer.Deserialize(this.DataApi.GetString(6, reader), type) as Notification;
            Assert.IsNotNull((object)notification, "notification");
            notification.ID = id;
            notification.Uri = new ItemUri(itemID, language, version, this.DatabaseName);
            notification.Processed = boolean;
            return notification;
        }
    }
}


