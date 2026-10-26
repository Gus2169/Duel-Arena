using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DuelArena.UI
{
    /// <summary>
    /// Le sélecteur des maquettes de l'utilisateur (2026-10-17), « ‹ Non › » : une valeur entre deux flèches,
    /// qui passe au choix suivant ou précédent. Remplace les cases à cocher et les listes déroulantes des
    /// Options, et sert aux réglages du salon.
    ///
    /// La valeur est le rang du choix (`ChangeEvent&lt;int&gt;`, comme un champ). Les choix fixes s'écrivent en
    /// UXML (`choices-text="Non;Oui"`), les autres se donnent en C# (`Choices`). Sans `wrap`, les flèches
    /// s'arrêtent aux bouts et s'éteignent.
    /// </summary>
    [UxmlElement]
    public partial class OptionCycler : VisualElement, INotifyValueChanged<int>
    {
        private readonly Button previous;
        private readonly Button next;
        private readonly Label label;
        private readonly List<string> choices = new();
        private int index;
        private bool wrap;

        public OptionCycler()
        {
            AddToClassList("option-cycler");
            previous = NewArrow("option-cycler__arrow--prev", -1);
            label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("option-cycler__value");
            next = NewArrow("option-cycler__arrow--next", 1);
            Add(previous);
            Add(label);
            Add(next);
            Refresh();
        }

        /// <summary>Les choix fixes, séparés par des points-virgules (« Non;Oui »).</summary>
        [UxmlAttribute]
        public string choicesText
        {
            get => string.Join(";", choices);
            set => Choices = string.IsNullOrEmpty(value) ? new string[0] : value.Split(';');
        }

        /// <summary>Après le dernier choix, revenir au premier (et inversement).</summary>
        [UxmlAttribute]
        public bool loop
        {
            get => wrap;
            set { wrap = value; Refresh(); }
        }

        public IReadOnlyList<string> Choices
        {
            get => choices;
            set
            {
                choices.Clear();
                if (value != null) choices.AddRange(value);
                index = Mathf.Clamp(index, 0, Mathf.Max(0, choices.Count - 1));
                Refresh();
            }
        }

        public int value
        {
            get => index;
            set
            {
                int clamped = Clamp(value);
                if (clamped == index) return;
                int old = index;
                SetValueWithoutNotify(clamped);
                using ChangeEvent<int> evt = ChangeEvent<int>.GetPooled(old, clamped);
                evt.target = this;
                SendEvent(evt);
            }
        }

        public void SetValueWithoutNotify(int newValue)
        {
            index = Clamp(newValue);
            Refresh();
        }

        private int Clamp(int v) => choices.Count == 0 ? 0 : Mathf.Clamp(v, 0, choices.Count - 1);

        private Button NewArrow(string modifier, int step)
        {
            var arrow = new Button(() => Step(step)) { focusable = false };
            arrow.AddToClassList("option-cycler__arrow");
            arrow.AddToClassList(modifier);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("option-cycler__icon");
            arrow.Add(icon);
            return arrow;
        }

        private void Step(int step)
        {
            if (choices.Count == 0) return;
            int target = index + step;
            if (wrap) target = (target % choices.Count + choices.Count) % choices.Count;
            value = target;
        }

        private void Refresh()
        {
            label.text = choices.Count > 0 ? choices[index] : "";
            previous.SetEnabled(choices.Count > 1 && (wrap || index > 0));
            next.SetEnabled(choices.Count > 1 && (wrap || index < choices.Count - 1));
        }
    }
}
