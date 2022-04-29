using UnityEngine;


[RequireComponent(typeof(Collider2D))]
public class InteractSimple : MonoBehaviour, IInteractable
{
	public string m_sceneName; // TODO: less error-prone type?

	public Sprite m_dialogueSprite;
	public WeightedObject<string[]>[] m_dialogue;


	public bool CanInteract(KinematicCharacter interactor) => !string.IsNullOrEmpty(m_sceneName) || (m_dialogue != null && m_dialogue.Length > 0 && !GameController.Instance.m_dialogueController.IsPlaying);


	public void Interact(KinematicCharacter interactor)
	{
		if (m_dialogue != null && m_dialogue.Length > 0)
		{
			GameController.Instance.m_dialogueController.Play(m_dialogueSprite, GetComponent<SpriteRenderer>().color, m_dialogue.RandomWeighted(), null, interactor.GetComponent<AvatarController>());
		}

		if (!string.IsNullOrEmpty(m_sceneName))
		{
			GameController.Instance.LoadScene(m_sceneName);
		}
	}
}
