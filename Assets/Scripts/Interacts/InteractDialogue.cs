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

	private bool m_preconditionResult; // TODO: replace w/ Tuple argument to preconditions?


	private void Start()
	{
		m_isVice = Random.value > 0.5f; // TODO: choose exactly one NPC
	}


	public bool CanInteract(KinematicCharacter interactor) => !GameController.Instance.m_dialogueController.IsPlaying;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		// lazy initialize dialogue options
		if (m_dialogueCombined == null)
		{
			m_dialogueCombined = GameController.Npcs[Index].SelectMany(source => source.m_dialogue).ToArray(); // NOTE the lack of deep-copying here, allowing the source NpcDialogue weights to be edited below and subsequently saved by GameController.Save() // TODO: avoid relying on runtime edits to ScriptableObject?
		}

		// filter dialogue options
		WeightedObject<NpcDialogue.Info>[] dialogueAllowed = m_dialogueCombined.Where(info =>
		{
			if (info.m_weight < 0.0f)
			{
				return false;
			}
			if (string.IsNullOrEmpty(info.m_object.m_preconditionName))
			{
				return true;
			}
			SendMessage(info.m_object.m_preconditionName, info.m_object);
			return m_preconditionResult;
		}).ToArray();

		// pick dialogue option
		NpcDialogue.Info dialogueCur = dialogueAllowed.FirstOrDefault(dialogue => dialogue.m_weight == 0.0f)?.m_object;
		if (dialogueCur == null)
		{
			dialogueCur = dialogueAllowed.RandomWeighted();
		}

		// play dialogue
		GameController.Instance.m_dialogueController.Play(m_dialogueSprite, GetComponent<SpriteRenderer>().color, dialogueCur.m_lines, interactor.GetComponent<AvatarController>(), dialogueCur.m_loop);

		// update weight
		// TODO: save across instantiations/sessions?
		WeightedObject<NpcDialogue.Info> weightedDialogueCur = m_dialogueCombined.First(dialogue => dialogue.m_object == dialogueCur); // TODO: support duplicate dialogue options?
		weightedDialogueCur.m_weight = weightedDialogueCur.m_object.m_singleUse ? -1.0f : weightedDialogueCur.m_weight == 0.0f ? 1.0f : weightedDialogueCur.m_weight * m_weightUseScalar;
	}

	// called via Interact()/SendMessage(NpcDialogue.Info.m_preconditionName)
	public void HasFinishedZone(NpcDialogue.Info dialogue)
	{
		m_preconditionResult = GameController.ZonesFinishedCount >= dialogue.m_userdata;
	}
}
