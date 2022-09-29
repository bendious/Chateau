using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractChute : MonoBehaviour, IInteractable
{
	[SerializeField] private Dialogue m_dialogue;


	public bool CanInteract(KinematicCharacter interactor) => enabled && !GameController.Instance.m_dialogueController.IsPlaying && !GameController.Instance.ActiveEnemiesRemain();

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		GameController.Instance.m_dialogueController.Play(m_dialogue.m_dialogue.RandomWeighted().m_lines, avatar: interactor.GetComponent<AvatarController>(), expressions: m_dialogue.m_expressions);
	}
}
