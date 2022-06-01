using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractScene : MonoBehaviour, IInteractable
{
	[SerializeField] private string m_sceneDestination; // TODO: drop-down list in Editor?


	public int Depth { private get; set; }


	public bool CanInteract(KinematicCharacter interactor) => !string.IsNullOrEmpty(m_sceneDestination);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		if (Depth > GameController.ZonesFinishedCount + 1)
		{
			// TODO: indicate w/ visual/audio rather than dialogue?
			GameController.Instance.m_dialogueController.Play(null, Color.white, new DialogueController.Line[] { new DialogueController.Line { m_text = "Locked." } }, interactor.GetComponent<AvatarController>());
		}
		else
		{
			// start animations and wait for trigger to call LoadScene()
			interactor.GetComponent<AvatarController>().DisablePlayerControl(); // TODO: avatar animation
			GetComponent<Animator>().SetTrigger("activate");
		}
	}

	public void LoadScene()
	{
		if (string.IsNullOrEmpty(m_sceneDestination))
		{
			return;
		}
		GameController.Instance.LoadScene(m_sceneDestination);
	}
}
