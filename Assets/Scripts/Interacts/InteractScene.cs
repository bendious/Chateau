using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractScene : MonoBehaviour, IInteractable
{
	[SerializeField] private string m_sceneDestination; // TODO: drop-down list in Editor?


	public int Depth { private get; set; }


	public void Interact(KinematicCharacter interactor)
	{
		if (Depth > GameController.ZonesFinishedCount + 1)
		{
			// TODO: indicate w/ visual/audio rather than dialogue?
			GameController.Instance.m_dialogueController.Play(null, Color.white, new string[] { "Locked." }, null, interactor.GetComponent<AvatarController>());
		}
		else
		{
			GameController.Instance.LoadScene(m_sceneDestination);
		}
	}
}
