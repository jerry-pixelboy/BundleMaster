#BundleMaster

AssetBundle Build and Manager Utility.

## Dependencies

- QUility https://git.coding.net/pixelboy/QUtility.git

### Firstly Init AssetBundleManager
```
IEnumerator Start()
{
	 yield return StartCoroutine(AssetBundleManager.InitManager());
}
```

### Load scene from assetbundle

1. load scene named TestScene from assetbundle named scene_bundle_testscene 
```
SceneLoader.LoadAsync("scene_bundle_91001", "91001", true, SceneLoadedCallback);
```

2. scene loaded callback
```
	void SceneLoadedCallback(string sceneName, AsyncOperation async)
    {
        async.allowSceneActivation = true;
        var loadedScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(loadedScene);
    }
```

3. scene load progress.
```
SceneLoader.progess
```

### Load asset from assetbundle, then instantiate it.
```
	AssetLoader.LoadAsync("ui_bundle_skillroot", "SkillRoot", delegate (Object obj)
	{
    	GameObject go = GameObject.Instantiate(obj) as GameObject;
        go.transform.SetParent(GameObject.FindObjectOfType<Canvas>().transform, false);
    });
```

### Unload loaded assetbundle from memory.
`AssetBundleManager.UnloadAssetBundle("scene_bundle_assetbundlewithshader");`
