using System;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractSimple : MonoBehaviour, IInteractable
{
	[SerializeField]
	private bool m_sceneChange;

	[SerializeField]
	private Sprite m_dialogueSprite;
	[SerializeField]
	private NpcDialogue[] m_dialogueSources;

	[SerializeField]
	private float m_weightUseScalar = 0.25f;


	private bool m_isVice;

	private WeightedObject<string[]>[] m_dialogueCombined;


	private void Start()
	{
		m_isVice = UnityEngine.Random.value > 0.5f; // TODO: choose exactly one NPC

		m_dialogueCombined = m_dialogueSources.SelectMany(source => source.m_dialogue.Select(dialogue => new WeightedObject<string[]> { m_object = dialogue.m_object, m_weight = dialogue.m_weight })).ToArray(); // NOTE the copy to prevent affecting source object weights later
	}


	public bool CanInteract(KinematicCharacter interactor) => m_sceneChange || (HasDialogue && !GameController.Instance.m_dialogueController.IsPlaying);

	public void Interact(KinematicCharacter interactor)
	{
		if (HasDialogue)
		{
			// pick and play dialogue
			string[] dialogueCur = m_dialogueCombined.FirstOrDefault(dialogue => dialogue.m_weight == 0.0f)?.m_object;
			if (dialogueCur == null)
			{
				dialogueCur = m_dialogueCombined.RandomWeighted();
			}
			GameController.Instance.m_dialogueController.Play(m_dialogueSprite, GetComponent<SpriteRenderer>().color, dialogueCur, null, interactor.GetComponent<AvatarController>());

			// update weight
			// TODO: save across instantiations/sessions?
			WeightedObject<string[]> weightedDialogueCur = m_dialogueCombined.First(dialogue => dialogue.m_object == dialogueCur); // TODO: support duplicate dialogue options?
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


	private bool HasDialogue => m_dialogueSources != null && m_dialogueSources.Length > 0;
}
