using System.Collections;
using System.Collections.Generic;
using Platformer.Mechanics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class InventoryController : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
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

	public void OnPointerClick(PointerEventData eventData)
	{
		// ignore if we're empty or the player is dragging
		if (!m_draggable || eventData.dragging)
		{
			return;
		}

		if (transform.GetSiblingIndex() > 1) // NOTE the 1-based indexing due to template object
		{
			SwapWithIndex(1);
		}
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

			// get other index and swap
			int endIdx = endElement.transform.GetSiblingIndex();
			SwapWithIndex(endIdx);
			return;
		}

		// if no swap, slide to old position
		StartCoroutine(LerpToRest());
	}


	private void SwapWithIndex(int index2)
	{
		int index1 = transform.GetSiblingIndex();
		Assert.AreNotEqual(index1, index2);
		InventoryController element2 = transform.parent.GetChild(index2).GetComponent<InventoryController>();

		// swap inventory order
		transform.SetSiblingIndex(index2);
		element2.transform.SetSiblingIndex(index1);

		// swap avatar hold order
		// NOTE that the indices are off by one between the inventory and avatar due to the inventory template object
		AvatarController avatar = Camera.main.GetComponent<GameController>().m_avatar;
		int idx1Final = index1 - 1;
		int idx2Final = index2 - 1;
		Transform tf1 = avatar.transform.GetChild(idx1Final);
		Transform tf2 = avatar.transform.GetChild(idx2Final);
		tf1.SetSiblingIndex(idx2Final);
		tf2.SetSiblingIndex(idx1Final);

		// swap icon positions
		Vector3 tmp = m_restPosition;
		m_restPosition = element2.m_restPosition;
		element2.m_restPosition = tmp;

		// slide to new positions
		StartCoroutine(LerpToRest());
		StartCoroutine(element2.LerpToRest());
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
