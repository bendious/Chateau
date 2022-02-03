using System;
using System.Collections;
using System.Collections.Generic;
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
		List<RaycastResult> results = new();
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

		// swap avatar hold
		// get items BEFORE editing attachments
		AvatarController avatar = GameController.Instance.m_avatar;
		Tuple<ItemController, IHolder> itemAndHolder1 = ItemFromIndex(avatar, index1);
		ItemController item1 = itemAndHolder1.Item1;
		Tuple<ItemController, IHolder> itemAndHolder2 = ItemFromIndex(avatar, index2);
		ItemController item2 = itemAndHolder2.Item1;

		// detach (to prevent too-many-to-hold failed attachment) and then attach
		if (item1 != null && item1.transform.parent != null)
		{
			item1.Detach(true);
		}
		if (item2 != null && item2.transform.parent != null)
		{
			item2.Detach(true);
		}
		if (item1 != null)
		{
			itemAndHolder2.Item2.ItemAttach(item1);
		}
		if (item2 != null)
		{
			itemAndHolder1.Item2.ItemAttach(item2);
		}

		// swap icon positions
		Vector3 tmp = m_restPosition;
		m_restPosition = element2.m_restPosition;
		element2.m_restPosition = tmp;

		// slide to new positions
		StartCoroutine(LerpToRest());
		StartCoroutine(element2.LerpToRest());
	}

	private Tuple<ItemController, IHolder> ItemFromIndex(Component character, int index)
	{
		// NOTE that the indices are off by one between the inventory and avatar due to the inventory template object
		int holderIdx = Math.Min(index, character.transform.childCount) - 1;
		Transform holderTf = character.transform.GetChild(holderIdx);
		int itemIdx = index - holderIdx - 1;
		Transform itemTf = holderTf.childCount > itemIdx ? holderTf.GetChild(itemIdx) : null;
		return Tuple.Create(itemTf == null ? null : itemTf.GetComponent<ItemController>(), holderTf.GetComponent<IHolder>());
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

		// refresh inventory if we're the last one done, to update colors/indices/etc.
		// TODO: efficiency?
		foreach (InventoryController slot in transform.parent.GetComponentsInChildren<InventoryController>())
		{
			if (slot.transform.position != slot.m_restPosition)
			{
				yield break;
			}
		}
		GameController.Instance.m_avatar.InventorySync();
	}
}
