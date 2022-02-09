using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


[RequireComponent(typeof(Button))]
public class MenuButtonController : MonoBehaviour, IPointerEnterHandler
{
	public void OnPointerEnter(PointerEventData eventData)
	{
		if (eventData.rawPointerPress == null && !EventSystem.current.alreadySelecting) // NOTE that alreadySelecting is just extra-cautious infinite-loop prevention, based on whether our position in the callstack is downstream of SetSelectedGameObject(), not whether anything is currently selected
		{
			EventSystem.current.SetSelectedGameObject(gameObject, eventData);
		}
	}
}
