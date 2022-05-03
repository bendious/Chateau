using System;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractSimple : MonoBehaviour, IInteractable
{
	public bool m_sceneChange;

	public Sprite m_dialogueSprite;
	public WeightedObject<string[]>[] m_dialogue;


	public bool CanInteract(KinematicCharacter interactor) => m_sceneChange || (m_dialogue != null && m_dialogue.Length > 0 && !GameController.Instance.m_dialogueController.IsPlaying);


	public void Interact(KinematicCharacter interactor)
	{
		if (m_dialogue != null && m_dialogue.Length > 0)
		{
			GameController.Instance.m_dialogueController.Play(m_dialogueSprite, GetComponent<SpriteRenderer>().color, m_dialogue.RandomWeighted(), null, interactor.GetComponent<AvatarController>());
		}

		if (m_sceneChange)
		{
			// https://stackoverflow.com/questions/40898310/get-name-of-next-scene-in-build-settings-in-unity3d
			string scenePath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == 0 ? 1 : 0);
			int sceneNameStart = scenePath.LastIndexOf("/", StringComparison.Ordinal) + 1; // Unity's asset paths always use '/' as a path separator
			int sceneNameEnd = scenePath.LastIndexOf(".", StringComparison.Ordinal);
			string sceneName = scenePath[sceneNameStart .. sceneNameEnd];
			GameController.Instance.LoadScene(sceneName);
		}
	}
}
