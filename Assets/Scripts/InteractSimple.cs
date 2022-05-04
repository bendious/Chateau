using System;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractSimple : MonoBehaviour, IInteractable
{
	public bool m_sceneChange;

	public Sprite m_dialogueSprite;
	public WeightedObject<string[]>[] m_dialogue;


	[SerializeField]
	private float m_weightUseScalar = 0.25f;


	public bool CanInteract(KinematicCharacter interactor) => m_sceneChange || (m_dialogue != null && m_dialogue.Length > 0 && !GameController.Instance.m_dialogueController.IsPlaying);

	public void Interact(KinematicCharacter interactor)
	{
		if (m_dialogue != null && m_dialogue.Length > 0)
		{
			// pick and play dialogue
			string[] dialogueCur = m_dialogue.FirstOrDefault(dialogue => dialogue.m_weight == 0.0f)?.m_object;
			if (dialogueCur == null)
			{
				dialogueCur = m_dialogue.RandomWeighted();
			}
			GameController.Instance.m_dialogueController.Play(m_dialogueSprite, GetComponent<SpriteRenderer>().color, dialogueCur, null, interactor.GetComponent<AvatarController>());

			// update weight
			// TODO: save across instantiations/sessions?
			WeightedObject<string[]> weightedDialogueCur = m_dialogue.First(dialogue => dialogue.m_object == dialogueCur); // TODO: support duplicate dialogue options?
			weightedDialogueCur.m_weight = weightedDialogueCur.m_weight == 0.0f ? 1.0f : weightedDialogueCur.m_weight * m_weightUseScalar;
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
