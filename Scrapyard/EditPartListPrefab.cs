using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Scrapyard
{
    /// <summary>
    ///  Courtesy of xEvilReaperx, edited by Sirius Sam
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    class EditPartListPrefab : MonoBehaviour
    {
        enum PartType { Part, Resource, Folder}

        class ClickListener : MonoBehaviour
        {
            AvailablePart myPart;
            EditorPartIcon icon;
            SpriteText qty, title;
            bool experimental;
            PartType type;

            public int QtyOnAssembly
            {
                get {
                    ShipConstruct construct = EditorLogic.fetch.ship;
                    if(construct != null) {
                        if(type == PartType.Resource) {
                            double total = 0;
                            foreach(Part P in construct.Parts) foreach (PartResource R in P.Resources) if(R.resourceName == myPart.name) total += R.amount;
                            return (int)total;
                        } else return construct.Parts.FindAll(p => p.partInfo.name == myPart.name).Count;
                    } else return 0;
                }
            }
            public int QtyAvailable { get { return Scrapyard.Instance != null ? (int)(type == PartType.Resource ? Scrapyard.Instance.Resources: Scrapyard.Instance.Parts).Get(myPart.name) : 0; } }

            void Start()
            {

                myPart = GetComponent<EditorPartIcon>().partInfo;
                icon = GetComponent<EditorPartIcon>();
                qty = transform.Find("CounterLabel").GetComponent<SpriteText>();
                experimental = Scrapyard.isExperimental(myPart);
                type = myPart.name == "Resources" ? PartType.Folder : Scrapyard.isResource(myPart.name) ? PartType.Resource : PartType.Part;

                if (type == PartType.Folder)
                {
                    icon.SetGrey("Resources cannot be assembled.");
                    qty.transform.Translate(new Vector3(-0.8f * EditorPartList.Instance.iconSize, EditorPartList.Instance.iconSize * 0.85f, 0), Space.Self);
                    qty.SetAnchor(SpriteText.Anchor_Pos.Upper_Left);
                    qty.alignment = SpriteText.Alignment_Type.Left;
                    GetComponent<UIButton>().AddValueChangedDelegate(TankClicked);
                }
                else if (type == PartType.Resource)
                {
                    icon.SetGrey("Resources cannot be assembled.");
                    title = transform.Find("TitleLabel").GetComponent<SpriteText>();
                    title.Text = myPart.name.Length>=10? myPart.name.Substring(0,10):myPart.name;
                    GetComponent<UIButton>().AddValueChangedDelegate(ResourceClicked);
                }
                UpdateCounter();
            }

            void TankClicked(IUIObject obj)
            {
                Scrapyard.resourceDisplay = Resource_Display.Detail;
                EditorPartList.Instance.Refresh();
            }
            void ResourceClicked(IUIObject obj)
            {
                Scrapyard.resourceDisplay = Resource_Display.Summary;
                EditorPartList.Instance.Refresh();
            }

            public void UpdateCounter()
            {
                if (type == PartType.Folder)
                {
                    int i = 0;
                    foreach (string s in Scrapyard.Instance.Resources.List.Keys)
                    {
                        if (i++ > 4) continue;
                        float a = 0;
                        foreach (Part P in EditorLogic.fetch.ship.Parts) foreach (PartResource R in P.Resources) if (R.resourceName == s) a += (float)R.amount;
                        float b = Scrapyard.Instance.Resources.Get(s), total = b - a;
                        qty.Text += String.Format(total < 0 ? Scrapyard.TankMissingFormat : Scrapyard.TankFormat,
                            a, b, total, (int)(-total * Scrapyard.getResourcePrice(s)), Scrapyard.getResourceCode(s));
                    }
                }
                else
                {
                    int a = QtyOnAssembly, b = QtyAvailable, total = b - a;
                    if (experimental && total <= 0) icon.SetGrey("Ran out of this item");
                    if (total < 0)
                    {
                        qty.Color = Color.red;
                        qty.Text = String.Format(Scrapyard.MissingQtyFormat, a, b, total, (int)(-total * (type == PartType.Resource ? Scrapyard.getResourcePrice(myPart.name) : Scrapyard.getPartRawPrice(myPart))) );
                    }
                    else
                    {
                        qty.Color = Color.white;
                        qty.Text = String.Format(Scrapyard.QtyFormat, a,b,total);
                    }
                }
            }
        }
        

        public static EditPartListPrefab Instance { private set; get; }
        void Start()
        {
            // VAB events
            GameEvents.onPartAttach.Add(RefreshList);
            GameEvents.onPartRemove.Add(RefreshList);


            var iconPrefab = EditorPartList.Instance.iconPrefab.gameObject;
            if (iconPrefab.GetComponent<ClickListener>() == null)
            {
                // Quantity
                GameObject labelHolder = new GameObject("CounterLabel");
                var label = labelHolder.AddComponent<SpriteText>();
                label.RenderCamera = Camera.allCameras.Where(c => (c.cullingMask & (1 << labelHolder.layer)) != 0).Single();
                labelHolder.layer = LayerMask.NameToLayer("EzGUI_UI");
                labelHolder.transform.parent = iconPrefab.transform;
                labelHolder.transform.localPosition = Vector3.zero;
                labelHolder.transform.Translate(new Vector3(EditorPartList.Instance.iconSize * 0.35f, EditorPartList.Instance.iconSize * -0.425f, label.RenderCamera.nearClipPlane - labelHolder.transform.position.z - 1f), Space.Self);
                label.Text = String.Empty;
                label.alignment = SpriteText.Alignment_Type.Right;
                label.font = UIManager.instance.defaultFont;
                label.renderer.sharedMaterial = UIManager.instance.defaultFontMaterial;
                label.SetColor(Color.white);
                label.SetAnchor(SpriteText.Anchor_Pos.Lower_Right);
                label.SetCharacterSize(12f);
                DontDestroyOnLoad(labelHolder);

                // Title of resources
                GameObject titleHolder = new GameObject("TitleLabel");
                var title = titleHolder.AddComponent<SpriteText>();
                title.RenderCamera = Camera.allCameras.Where(c => (c.cullingMask & (1 << titleHolder.layer)) != 0).Single();
                titleHolder.layer = LayerMask.NameToLayer("EzGUI_UI");
                titleHolder.transform.parent = iconPrefab.transform;
                titleHolder.transform.localPosition = Vector3.zero;
                titleHolder.transform.Translate(new Vector3(-EditorPartList.Instance.iconSize * 0.425f, EditorPartList.Instance.iconSize * 0.425f, title.RenderCamera.nearClipPlane - titleHolder.transform.position.z - 1f), Space.Self);
                title.Text = String.Empty;
                title.alignment = SpriteText.Alignment_Type.Left;
                title.font = UIManager.instance.defaultFont;
                title.renderer.sharedMaterial = UIManager.instance.defaultFontMaterial;
                title.SetColor(Color.white);
                title.SetAnchor(SpriteText.Anchor_Pos.Upper_Left);
                title.SetCharacterSize(12f);
                DontDestroyOnLoad(titleHolder);

                iconPrefab.AddComponent<ClickListener>();
            }
            EditorPartList.Instance.ExcludeFilters.AddFilter(new EditorPartListFilter("Scrapyard", FilterPart));
            EditorPartList.Instance.Refresh();
        }

        void OnDestroy()
        {
            GameEvents.onPartAttach.Remove(RefreshList);
            GameEvents.onPartRemove.Remove(RefreshList);
        }

        /// <summary>
        /// Parts will be displayed if:
        /// 1. they are accessible in the research tree
        /// 2. they are not accessible but some quantity is available in the inventory.
        /// </summary>
        /// <param name="part">Part to test</param>
        /// <returns>true if the part should be displayed in the list</returns>
        public bool FilterPart(AvailablePart part)
        {
            if (part.name == "Resources")
            {
                return Scrapyard.Instance.recoverResources && Scrapyard.resourceDisplay == Resource_Display.Summary;
            }
            else if (Scrapyard.hasResourceCode(part.name))
            {
                return Scrapyard.Instance.recoverResources && Scrapyard.resourceDisplay == Resource_Display.Detail && Scrapyard.Instance.Resources.Get(part.name) != 0;
            }
            else if (Scrapyard.isExperimental(part))
            {
                int qty = (int)Scrapyard.Instance.Parts.Get(part.name);
                bool available = qty > 0;
                if (available && !ResearchAndDevelopment.IsExperimentalPart(part)) ResearchAndDevelopment.AddExperimentalPart(part);
                return available;
            }
            else return true;
        }

        void Update()
        {
            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                Part selected = EditorLogic.SelectedPart;
                // If a part is selected, and it's not the StartPod
                if (selected != null && selected != EditorLogic.startPod)
                {
                    // is it possible to place it?
                    ShipConstruct construct = EditorLogic.fetch.ship;
                    Dictionary<AvailablePart, int> selectedParts = Scrapyard.ListAllParts(selected);

                    foreach (AvailablePart id in selectedParts.Keys)
                    {
                        // this part is experimental
                        if (ResearchAndDevelopment.GetTechnologyState(id.TechRequired) != RDTech.State.Available)
                        {
                            int a = (int)Scrapyard.Instance.Parts.Get(id.name), b = construct.Parts.FindAll(x => x.partInfo.name == id.name).Count, c = selectedParts[id];
                            int available = a - b - c;
                            if (available < 0)
                            {
                                EditorLogic.fetch.DestroySelectedPart();
                                selected = null;
                            }
                        }
                    }
                }
            }
        }

        void RefreshList(GameEvents.HostTargetAction<Part, Part> action)
        {
            if (EditorPartList.Instance != null) EditorPartList.Instance.Refresh();
        }

    }
}
