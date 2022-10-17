namespace Cuervo

module Say =
    let hello name =
        printfn "Hello %s" name

    open Corax
    open Voron.Util.Settings
    open Voron


    // 
    //protected StorageEnvironmentOptions(
    //  VoronPathSetting tempPath, 
    //  IoChangesNotifications ioChangesNotifications, 
    //  CatastrophicFailureNotification catastrophicFailureNotification)

    // public VoronPathSetting(string path, string baseDataDir = null)
    let tempPath = new VoronPathSetting(path = "", baseDataDir = "")
    let options = StorageEnvironmentOptions.CreateMemoryOnly()
    //let storageEnvOptions : StorageEnvironmentOptions = new StorageEnvironmentOptions
    //let env : StorageEnvironment = new StorageEnvironment storageEnvOptions

    //let searcher = new IndexSearcher(environment = env)
    //var match1 = searcher.TermQuery("Id", "entry/1");
