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