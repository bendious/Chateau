using UnityEngine;


public class CreditsController : MonoBehaviour
{
	[SerializeField] private float m_ctBackdropRadius = 1.0f;


	private float m_cameraSize;
	private RoomController m_roomCurrent;


	private void Start()
	{
		m_cameraSize = GameController.Instance.m_vCamMain.GetCinemachineComponent<Cinemachine.CinemachineFramingTransposer>().m_MinimumOrthoSize;
		StartCoroutine(SetInitialRoomDelayed());
	}


	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnForward(UnityEngine.InputSystem.InputValue input)
	{
		UpdateRoom(room => room.FirstChild);
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnBack(UnityEngine.InputSystem.InputValue input)
	{
		UpdateRoom(room => room.Parent);
	}


	private System.Collections.IEnumerator SetInitialRoomDelayed()
	{
		yield return new WaitForEndOfFrame(); // due to GameController.m_startRoom not being immediately available
		UpdateRoom(null);
	}

	private void UpdateRoom(System.Func<RoomController, RoomController> roomToNext)
	{
		if (m_roomCurrent != null)
		{
			GameController.Instance.RemoveCameraTargets(m_roomCurrent.transform, m_roomCurrent.m_backdrop.transform);
		}

		RoomController roomNext = roomToNext == null ? null : roomToNext(m_roomCurrent);

		m_roomCurrent = roomNext != null ? roomNext : GameController.Instance.RoomFromPosition(Vector2.zero);

		GameController.Instance.AddCameraTargetsSized(m_ctBackdropRadius, m_roomCurrent.m_backdrop.transform);
		if (m_roomCurrent.Bounds.extents.y > m_cameraSize)
		{
			GameController.Instance.AddCameraTargets(m_roomCurrent.transform);
		}
	}
}
