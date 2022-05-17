using System;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractScene : MonoBehaviour, IInteractable
{
	public void Interact(KinematicCharacter interactor)
	{
		// https://stackoverflow.com/questions/40898310/get-name-of-next-scene-in-build-settings-in-unity3d
		string scenePath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == 0 ? 1 : 0);
		int sceneNameStart = scenePath.LastIndexOf("/", StringComparison.Ordinal) + 1; // Unity's asset paths always use '/' as a path separator
		int sceneNameEnd = scenePath.LastIndexOf(".", StringComparison.Ordinal);
		string sceneName = scenePath[sceneNameStart .. sceneNameEnd];
		GameController.Instance.LoadScene(sceneName);
	}
}
