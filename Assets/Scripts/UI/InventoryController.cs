using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;


[DisallowMultipleComponent]
public class InventoryController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	public float m_smoothTime = 0.05f;
	public float m_smoothEpsilon = 0.01f;
	public GraphicRaycaster m_raycaster;
	public TMP_Text m_tooltip;
	public bool m_draggable = false;


	private Vector2 m_restPosition;
	private Vector2 m_mouseOffset;
	private Vector2 m_lerpVelocity;


	private void Start()
	{
		m_restPosition = GetComponent<RectTransform>().anchoredPosition;
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		if (m_draggable)
		{
			Activate(!eventData.dragging);
		}
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		Deactivate();
	}

	public void OnPointerClick(PointerEventData eventData)
	{
		// ignore if we're empty or the player is dragging
		if (!m_draggable || eventData.dragging)
		{
			return;
		}

		switch (eventData.button)
		{
			case PointerEventData.InputButton.Left:
				{
					ItemController item = ItemFromIndex(transform.root, transform.GetSiblingIndex()).m_item;
					if (item != null && item.gameObject.activeSelf)
					{
						item.Swing(false); // TODO: send both events?
					}
				}
				break;
			case PointerEventData.InputButton.Right:
				{
					ItemController item = ItemFromIndex(transform.root, transform.GetSiblingIndex()).m_item;
					if (item != null)
					{
						item.Use();
					}
				}
				break;
			case PointerEventData.InputButton.Middle:
				if (transform.GetSiblingIndex() > 1) // NOTE the 1-based indexing due to template object
				{
					SwapWithIndex(0);
				}
				break;
			default:
				break;
		}
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		if (!m_draggable)
		{
			eventData.pointerDrag = null; // this cancels OnDrag{End}() being called
			return;
		}
		m_tooltip.gameObject.SetActive(false);
		m_mouseOffset = GetComponent<RectTransform>().anchoredPosition - eventData.position;
	}

	public void OnDrag(PointerEventData eventData)
	{
		GetComponent<RectTransform>().anchoredPosition = eventData.position + m_mouseOffset;
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
			SwapWithIndex(endElement.transform.GetSiblingIndex() - 1); // -1 due to template object
			return;
		}

		// if far enough outside inventory area, detach
		RectTransform rectTf = GetComponent<RectTransform>();
		if (eventData.position.y > m_restPosition.y + 2.0f * rectTf.sizeDelta.y)
		{
			SlotItemInfo slotItemInfo1 = ItemFromIndex(transform.root, transform.GetSiblingIndex());
			slotItemInfo1.m_item.Detach(false);

			// reset/update display
			rectTf.anchoredPosition = m_restPosition; // NOTE that we don't lerp here since the slot is now empty
			return;
		}

		// if no swap, slide to old position
		StartCoroutine(LerpToRest());
	}


	public void Activate(bool tooltip)
	{
		ChangeActivation("UI", 1.0f, tooltip);
	}

	public void Deactivate()
	{
		ChangeActivation("Avatar", 0.5f, false); // TODO: check for active menus/overlays?
	}

	public void SwapWithIndex(int index2)
	{
		int siblingIndex1 = transform.GetSiblingIndex();
		int siblingIndex2 = index2 + 1; // +1 due to deactivated template object
		Assert.AreNotEqual(siblingIndex1, siblingIndex2);
		InventoryController element2 = transform.parent.GetChild(siblingIndex2).GetComponent<InventoryController>();

		// swap inventory order
		transform.SetSiblingIndex(siblingIndex2);
		element2.transform.SetSiblingIndex(siblingIndex1);

		// swap avatar hold
		// get items BEFORE editing attachments
		Transform avatarTf = transform.root;
		SlotItemInfo slotItemInfo1 = ItemFromIndex(avatarTf, siblingIndex1);
		ItemController item1 = slotItemInfo1.m_item;
		SlotItemInfo slotItemInfo2 = ItemFromIndex(avatarTf, siblingIndex2);
		ItemController item2 = slotItemInfo2.m_item;

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
			slotItemInfo2.m_holder.ChildAttach(item1);
		}
		if (item2 != null)
		{
			slotItemInfo1.m_holder.ChildAttach(item2);
		}
		if (item1 != null)
		{
			item1.transform.SetSiblingIndex(slotItemInfo2.m_holderIndex);
		}
		if (item2 != null)
		{
			item2.transform.SetSiblingIndex(slotItemInfo1.m_holderIndex);
		}

		// swap icon positions
		Vector3 tmp = m_restPosition;
		m_restPosition = element2.m_restPosition;
		element2.m_restPosition = tmp;

		// slide to new positions
		StartCoroutine(LerpToRest());
		StartCoroutine(element2.LerpToRest());
	}


	private struct SlotItemInfo
	{
		public ItemController m_item;
		public IHolder m_holder;
		public int m_holderIndex;
	}


	private SlotItemInfo ItemFromIndex(Component character, int index)
	{
		// split index into holder/item indices
		IHolder[] holders = character.GetComponentsInChildren<IHolder>().Where(holder => holder.Component.gameObject != character.gameObject).ToArray();
		int itemIdx = index - 1; // NOTE that the indices are off by one between the inventory and avatar due to the inventory template object
		int holderIdx = 0;
		foreach (IHolder holderItr in holders)
		{
			if (itemIdx < holderItr.HoldCountMax)
			{
				break;
			}
			itemIdx -= holderItr.HoldCountMax;
			++holderIdx;
		}

		// get info
		IHolder holder = holders[holderIdx];
		Transform holderTf = holder.Component.transform;
		Transform itemTf = holderTf.childCount > itemIdx ? holderTf.GetChild(itemIdx) : null;
		return new SlotItemInfo { m_item = itemTf == null ? null : itemTf.GetComponent<ItemController>(), m_holder = holder, m_holderIndex = itemIdx };
	}

	private IEnumerator LerpToRest()
	{
		m_lerpVelocity = Vector2.zero;

		RectTransform rectTf = GetComponent<RectTransform>();
		while ((rectTf.anchoredPosition - m_restPosition).sqrMagnitude > m_smoothEpsilon)
		{
			rectTf.anchoredPosition = rectTf.anchoredPosition.SmoothDamp(m_restPosition, ref m_lerpVelocity, m_smoothTime);
			yield return null;
		}

		rectTf.anchoredPosition = m_restPosition;

		// refresh inventory if we're the last one done, to update colors/indices/etc.
		// TODO: efficiency?
		foreach (InventoryController slot in transform.parent.GetComponentsInChildren<InventoryController>())
		{
			if (slot.GetComponent<RectTransform>().anchoredPosition != slot.m_restPosition)
			{
				yield break;
			}
		}
		transform.root.GetComponent<AvatarController>().InventorySync();
	}

	private void ChangeActivation(string actionMapName, float alpha, bool tooltip)
	{
		transform.root.GetComponent<PlayerInput>().SwitchCurrentActionMap(actionMapName);
		Image image = GetComponent<Image>();
		image.color = new Color(image.color.r, image.color.g, image.color.b, alpha);
		m_tooltip.gameObject.SetActive(tooltip);
	}
}
