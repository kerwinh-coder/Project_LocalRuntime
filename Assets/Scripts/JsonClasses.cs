using System;
using System.Collections.Generic;
using System.Linq;

[Serializable]
public class BasicServant
{
    public List<BasicServantEntry> Entries = new List<BasicServantEntry>();

    public List<Costume> GetAllCostumes()
    {
        return Entries.SelectMany(e => e.costumes).ToList();
    }

    [Serializable]
    public class BasicServantEntry
    {
        public string name;
        public int id;
        public int collectionNo;
        public string type;
        public string flag;
        public string className;
        public string attribute;
        public int rarity;
        public int atkMax;
        public int hpMax;
        public string face;
        public Dictionary<string, Costume> costume;
        public List<Costume> costumes;
    }

    [Serializable]
    public class Costume
    {
        public int id;
        public int costumeCollectionNo;
        public int battleCharaId;
        public string name;
        public string shortName;
    }
}

[Serializable]
public class FileInput
{
    public string blob;
    public string name;
}
