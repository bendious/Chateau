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
	[SerializeField] private float m_smoothTime = 0.05f;
	[SerializeField] private float m_smoothEpsilon = Utility.FloatEpsilon;
	[SerializeField] private GraphicRaycaster m_raycaster;
	public TMP_Text m_itemText;
	[SerializeField] private TMP_Text m_tooltip;
	public TMP_Text m_tooltipPerItem;
	public bool m_draggable = false;

	[SerializeField] private Image m_damageMeter;
	[SerializeField] private Image m_weightMeter;
	[SerializeField] private Image m_speedMeter;


	private int m_templateOffset;
	private Vector2 m_restPosition;
	private Vector2 m_mouseOffset;
	private Vector2 m_lerpVelocity;


	private void Start()
	{
		m_templateOffset = transform.parent.GetComponentsInChildren<Transform>(true).First(tf => !tf.gameObject.activeSelf).GetSiblingIndex() + 1; // TODO: don't assume the template object is the first deactivated child?
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
					ItemController item = ItemFromIndex(transform.root, transform.GetSiblingIndex() - m_templateOffset).m_item;
					if (item != null && item.gameObject.activeSelf)
					{
						item.Swing(false); // TODO: send both events?
					}
				}
				break;
			case PointerEventData.InputButton.Right:
				{
					ItemController item = ItemFromIndex(transform.root, transform.GetSiblingIndex() - m_templateOffset).m_item;
					if (item != null)
					{
						item.Use(true); // TODO: also send release event?
					}
				}
				break;
			case PointerEventData.InputButton.Middle:
				if (transform.GetSiblingIndex() > m_templateOffset)
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
			SwapWithIndex(endElement.transform.GetSiblingIndex() - m_templateOffset);
			return;
		}

		// if far enough outside inventory area, detach
		RectTransform rectTf = GetComponent<RectTransform>();
		if (eventData.position.y > m_restPosition.y + 2.0f * rectTf.sizeDelta.y)
		{
			SlotItemInfo slotItemInfo1 = ItemFromIndex(transform.root, transform.GetSiblingIndex() - m_templateOffset);
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
		ItemController item = ItemFromIndex(transform.root, transform.GetSiblingIndex() - m_templateOffset).m_item; // TODO: don't assume avatars will never have a parent?
		if (item != null)
		{
			const float maxMeterPct = 2.0f; // TODO: parameterize/derive?
			const float referenceSpeed = 20.0f; // TODO: parameterize/derive?
			m_damageMeter.fillAmount = item.m_swingInfo.m_damage / maxMeterPct;
			m_weightMeter.fillAmount = item.GetComponent<Rigidbody2D>().mass / maxMeterPct;
			m_speedMeter.fillAmount = item.m_throwSpeed / referenceSpeed / maxMeterPct;
		}

		ChangeActivation(true, 1.0f, tooltip && item != null);
	}

	public void Deactivate()
	{
		ChangeActivation(false, 0.5f, false); // TODO: check for active menus/overlays?
	}

	public void SwapWithIndex(int index2)
	{
		int siblingIndex1 = transform.GetSiblingIndex();
		int siblingIndex2 = index2 + m_templateOffset;
		Assert.AreNotEqual(siblingIndex1, siblingIndex2);
		InventoryController element2 = transform.parent.GetChild(siblingIndex2).GetComponent<InventoryController>();

		// swap inventory order
		transform.SetSiblingIndex(siblingIndex2);
		element2.transform.SetSiblingIndex(siblingIndex1);

		// swap avatar hold
		// get items BEFORE editing attachments
		Transform avatarTf = transform.root;
		SlotItemInfo slotItemInfo1 = ItemFromIndex(avatarTf, siblingIndex1 - m_templateOffset);
		ItemController item1 = slotItemInfo1.m_item;
		SlotItemInfo slotItemInfo2 = ItemFromIndex(avatarTf, siblingIndex2 - m_templateOffset);
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
		(element2.m_restPosition, m_restPosition) = (m_restPosition, element2.m_restPosition);

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


	private static SlotItemInfo ItemFromIndex(Component character, int index)
	{
		// split index into holder/item indices
		IHolder[] holders = character.GetComponentsInChildren<IHolder>().Where(holder => holder.Component.gameObject != character.gameObject).ToArray();
		int holderIdx = 0;
		foreach (IHolder holderItr in holders)
		{
			if (index < holderItr.HoldCountMax)
			{
				break;
			}
			index -= holderItr.HoldCountMax;
			++holderIdx;
		}

		// get info
		IHolder holder = holders[holderIdx];
		Transform holderTf = holder.Component.transform;
		IAttachable[] siblings = holderTf.GetComponentsInDirectChildren<IAttachable>(a => a.Component, true).ToArray();
		Transform itemTf = siblings.Length > index ? siblings[index].Component.transform : null;
		return new() { m_item = itemTf == null ? null : itemTf.GetComponent<ItemController>(), m_holder = holder, m_holderIndex = index };
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

	private void ChangeActivation(bool active, float alpha, bool tooltip)
	{
		InputActionMap actionMap = transform.root.GetComponent<AvatarController>().ControlsUI; // TODO: cache? don't assume avatars will never have a parent object?
		if (active)
		{
			actionMap.Enable();
		}
		else
		{
			actionMap.Disable();
		}
		Image image = GetComponent<Image>();
		image.color = new(image.color.r, image.color.g, image.color.b, alpha);
		m_tooltip.gameObject.SetActive(tooltip);
	}
}
