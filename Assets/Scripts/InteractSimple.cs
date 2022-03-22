using UnityEngine;


[RequireComponent(typeof(Collider2D))]
public class InteractSimple : MonoBehaviour, IInteractable
{
	public string m_sceneName; // TODO: less error-prone type?

	public WeightedObject<string[]>[] m_dialogue;


	public bool CanInteract(KinematicCharacter interactor) => !string.IsNullOrEmpty(m_sceneName) || (m_dialogue != null && m_dialogue.Length > 0 && !GameController.Instance.m_dialogueController.IsPlaying);


	public void Interact(KinematicCharacter interactor)
	{
		if (m_dialogue != null && m_dialogue.Length > 0)
		{
			GameController.Instance.m_dialogueController.Play(Utility.RandomWeighted(m_dialogue), null);
		}

		if (!string.IsNullOrEmpty(m_sceneName))
		{
			GameController.Instance.LoadScene(m_sceneName);
		}
	}
}
