using UnityEngine;


[CreateAssetMenu]
public class NpcDialogue : ScriptableObject
{
	[System.Serializable]
	public class Info
	{
		public DialogueController.Line[] m_lines;
		public bool m_loop;
	};
	public WeightedObject<Info>[] m_dialogue;
}
