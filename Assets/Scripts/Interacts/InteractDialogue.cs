using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractDialogue : MonoBehaviour, IInteractable
{
	[SerializeField]
	private Sprite m_dialogueSprite;

	[SerializeField]
	private float m_weightUseScalar = 0.25f;


	public int Index { private get; set; }


	private bool m_isVice;

	private WeightedObject<NpcDialogue.Info>[] m_dialogueCombined;


	private void Start()
	{
		m_isVice = Random.value > 0.5f; // TODO: choose exactly one NPC
	}


	public bool CanInteract(KinematicCharacter interactor) => !GameController.Instance.m_dialogueController.IsPlaying;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		if (m_dialogueCombined == null)
		{
			m_dialogueCombined = GameController.Npcs[Index].SelectMany(source => source.m_dialogue.Select(dialogue => new WeightedObject<NpcDialogue.Info> { m_object = dialogue.m_object, m_weight = dialogue.m_weight })).ToArray(); // NOTE the copy to prevent affecting source object weights later
		}

		// pick and play dialogue
		NpcDialogue.Info dialogueCur = m_dialogueCombined.FirstOrDefault(dialogue => dialogue.m_weight == 0.0f)?.m_object;
		if (dialogueCur == null)
		{
			dialogueCur = m_dialogueCombined.RandomWeighted();
		}
		GameController.Instance.m_dialogueController.Play(m_dialogueSprite, GetComponent<SpriteRenderer>().color, dialogueCur.m_lines, interactor.GetComponent<AvatarController>(), dialogueCur.m_loop);

		// update weight
		// TODO: save across instantiations/sessions?
		WeightedObject<NpcDialogue.Info> weightedDialogueCur = m_dialogueCombined.First(dialogue => dialogue.m_object == dialogueCur); // TODO: support duplicate dialogue options?
		weightedDialogueCur.m_weight = weightedDialogueCur.m_weight == 0.0f ? 1.0f : weightedDialogueCur.m_weight * m_weightUseScalar;
	}
}
