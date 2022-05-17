using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractDialogue : MonoBehaviour, IInteractable
{
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
		m_isVice = Random.value > 0.5f; // TODO: choose exactly one NPC

		m_dialogueCombined = m_dialogueSources.SelectMany(source => source.m_dialogue.Select(dialogue => new WeightedObject<string[]> { m_object = dialogue.m_object, m_weight = dialogue.m_weight })).ToArray(); // NOTE the copy to prevent affecting source object weights later
	}


	public bool CanInteract(KinematicCharacter interactor) => !GameController.Instance.m_dialogueController.IsPlaying;

	public void Interact(KinematicCharacter interactor)
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
}
