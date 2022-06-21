using UnityEngine;


[CreateAssetMenu]
public class NpcDialogue : ScriptableObject
{
	[System.Serializable]
	public class Info
	{
		public string m_preconditionName;
		public int m_userdata;
		public DialogueController.Line[] m_lines;
		public bool m_loop;
		public bool m_singleUse;
	};
	public WeightedObject<Info>[] m_dialogue;
}
