using System.Collections;
using System.Collections.Generic;
using Platformer.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class InventoryController : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	public float m_smoothTime = 0.05f;
	public float m_smoothEpsilon = 0.01f;

	public GraphicRaycaster m_raycaster;

	public bool m_draggable = false;


	private Vector3 m_restPosition;
	private Vector2 m_mouseOffset;

	private Vector3 m_lerpVelocity;


	private void Start()
	{
		m_restPosition = transform.position;
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		if (!m_draggable)
		{
			eventData.pointerDrag = null; // this cancels OnDrag{End}() being called
			return;
		}

		m_mouseOffset = (Vector2)transform.position - eventData.position;
	}

	public void OnDrag(PointerEventData eventData)
	{
		transform.position = eventData.position + m_mouseOffset;
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		// check graphics under pointer
		List<RaycastResult> results = new List<RaycastResult>();
		m_raycaster.Raycast(eventData, results);

		foreach (RaycastResult result in results)
		{
			// check validity
			InventoryController endElement = result.gameObject.GetComponent<InventoryController>();
			if (endElement == null || endElement == this)
			{
				continue;
			}

			// get indices
			int startIdx = transform.GetSiblingIndex();
			int endIdx = endElement.transform.GetSiblingIndex();

			// swap order
			transform.SetSiblingIndex(endIdx);
			endElement.transform.SetSiblingIndex(startIdx);
			AvatarController avatar = Camera.main.GetComponent<GameController>().m_avatar;
			Transform startTf = avatar.transform.GetChild(startIdx - 1);
			Transform endTf = avatar.transform.GetChild(endIdx - 1);
			startTf.SetSiblingIndex(endIdx - 1);
			endTf.SetSiblingIndex(startIdx - 1);

			// swap positions
			Vector3 tmp = m_restPosition;
			m_restPosition = endElement.m_restPosition;
			endElement.m_restPosition = tmp;
			StartCoroutine(endElement.LerpToRest());
			break;
		}

		// slide to old/new position
		StartCoroutine(LerpToRest());
	}


	private IEnumerator LerpToRest()
	{
		m_lerpVelocity = Vector3.zero;

		while ((transform.position - m_restPosition).sqrMagnitude > m_smoothEpsilon)
		{
			Vector3 newPos;
			newPos.x = Mathf.SmoothDamp(transform.position.x, m_restPosition.x, ref m_lerpVelocity.x, m_smoothTime);
			newPos.y = Mathf.SmoothDamp(transform.position.y, m_restPosition.y, ref m_lerpVelocity.y, m_smoothTime);
			newPos.z = Mathf.SmoothDamp(transform.position.z, m_restPosition.z, ref m_lerpVelocity.z, m_smoothTime);
			transform.position = newPos;

			yield return null;
		}

		transform.position = m_restPosition;
	}
}
