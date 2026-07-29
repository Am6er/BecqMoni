//------------------------------------------------------------------------------
// Сгенерировано integration/tools/gen_strings.py из RoiWizardStrings.resx.
// Править этот файл руками не нужно: правьте таблицу в gen_strings.py.
//------------------------------------------------------------------------------

using System.Globalization;
using System.Resources;

namespace BecquerelMonitor.RoiWizard
{
    // Подписи интерфейса модуля. Нейтральная таблица английская, рядом
    // RoiWizardStrings.ru.resx; MSBuild собирает сателлит, как и для остальных
    // форм BecqMoni. Новый язык — ещё один .resx, код не трогается.
    //
    // Если ресурс недоступен (тесты ядра собираются консольным компилятором
    // без ресурсов), возвращается английский текст, зашитый сюда генератором.
    internal static class RoiWizardStrings
    {
        static ResourceManager manager;

        static string Get(string key, string fallback)
        {
            try
            {
                if (manager == null)
                {
                    manager = new ResourceManager(
                        "BecquerelMonitor.RoiWizard.RoiWizardStrings",
                        typeof(RoiWizardStrings).Assembly);
                }
                string value = manager.GetString(key, CultureInfo.CurrentUICulture);
                return value ?? fallback;
            }
            catch (MissingManifestResourceException)
            {
                return fallback;
            }
        }

        public static string form_Title
        {
            get { return Get("form_Title", "ROI and nuclide set builder"); }
        }

        public static string tabSources_Text
        {
            get { return Get("tabSources_Text", "1 · Nuclides"); }
        }

        public static string tabLines_Text
        {
            get { return Get("tabLines_Text", "2 · Lines"); }
        }

        public static string tabExport_Text
        {
            get { return Get("tabExport_Text", "3 · Styling and creation"); }
        }

        public static string statusFormat
        {
            get { return Get("statusFormat", "lines: {0} of {1} · nuclides: {2}"); }
        }

        public static string buttonHelp_Text
        {
            get { return Get("buttonHelp_Text", "Help"); }
        }

        public static string stepBack
        {
            get { return Get("stepBack", "◂ Back"); }
        }

        public static string stepForward
        {
            get { return Get("stepForward", "Next ▸"); }
        }

        public static string stepNuclides
        {
            get { return Get("stepNuclides", "Nuclides"); }
        }

        public static string stepLines
        {
            get { return Get("stepLines", "Lines"); }
        }

        public static string stepExport
        {
            get { return Get("stepExport", "Styling and creation"); }
        }

        public static string groupSearch_Text
        {
            get { return Get("groupSearch_Text", "Nuclide search"); }
        }

        public static string buttonAddSingle_Text
        {
            get { return Get("buttonAddSingle_Text", "Add"); }
        }

        public static string buttonAddFamily_Text
        {
            get { return Get("buttonAddFamily_Text", "+ family"); }
        }

        public static string buttonAddChain_Text
        {
            get { return Get("buttonAddChain_Text", "+ chain"); }
        }

        public static string columnCatalogName_Text
        {
            get { return Get("columnCatalogName_Text", "Nuclide"); }
        }

        public static string columnCatalogFamilies_Text
        {
            get { return Get("columnCatalogFamilies_Text", "Families"); }
        }

        public static string columnCatalogLines_Text
        {
            get { return Get("columnCatalogLines_Text", "Lines"); }
        }

        public static string labelSearchHint_Text
        {
            get { return Get("labelSearchHint_Text", "Typing narrows the list: by name or by family code."); }
        }

        public static string presetsCaption
        {
            get { return Get("presetsCaption", "Presets:"); }
        }

        public static string preset1_Title
        {
            get { return Get("preset1_Title", "NORM background"); }
        }

        public static string preset1_Hint
        {
            get { return Get("preset1_Hint", "Th-232 + U-238 as chains + K-40"); }
        }

        public static string preset2_Title
        {
            get { return Get("preset2_Title", "Cs-137 / Co-60 check"); }
        }

        public static string preset2_Hint
        {
            get { return Get("preset2_Hint", "Reference check sources"); }
        }

        public static string preset3_Title
        {
            get { return Get("preset3_Title", "Calibration set"); }
        }

        public static string preset3_Hint
        {
            get { return Get("preset3_Hint", "Am-241, Ba-133, Eu-152, Cs-137, Co-60"); }
        }

        public static string preset4_Title
        {
            get { return Get("preset4_Title", "Medical"); }
        }

        public static string preset4_Hint
        {
            get { return Get("preset4_Hint", "MED family"); }
        }

        public static string preset5_Title
        {
            get { return Get("preset5_Title", "Detector and shield XRF"); }
        }

        public static string preset5_Hint
        {
            get { return Get("preset5_Hint", "Pb, W, La, Ba, I"); }
        }

        public static string groupGroup_Text
        {
            get { return Get("groupGroup_Text", "Group"); }
        }

        public static string buttonGroupAll_Text
        {
            get { return Get("buttonGroupAll_Text", "add all"); }
        }

        public static string buttonGroupFamily_Text
        {
            get { return Get("buttonGroupFamily_Text", "+ family lines"); }
        }

        public static string buttonGroupChain_Text
        {
            get { return Get("buttonGroupChain_Text", "+ chain"); }
        }

        public static string hintNone
        {
            get { return Get("hintNone", "Tick a nuclide — the buttons apply to it."); }
        }

        public static string hintPicked
        {
            get { return Get("hintPicked", "Applies to the ticked ones ({0})."); }
        }

        public static string groupXrf_Text
        {
            get { return Get("groupXrf_Text", "XRF elements"); }
        }

        public static string labelXrf_Text
        {
            get { return Get("labelXrf_Text", "Shielding and detector materials:"); }
        }

        public static string labelXrfHint_Text
        {
            get { return Get("labelXrfHint_Text", "Kα/Kβ (+L for heavy elements). Intensities are nominal (Kα1 = 100) — markers only."); }
        }

        public static string groupSelected_Text
        {
            get { return Get("groupSelected_Text", "Selected"); }
        }

        public static string buttonClear_Text
        {
            get { return Get("buttonClear_Text", "clear all"); }
        }

        public static string xrfChipPrefix
        {
            get { return Get("xrfChipPrefix", "XRF "); }
        }

        public static string emptySelectionHint
        {
            get { return Get("emptySelectionHint", "empty — start with a group above"); }
        }

        public static string groupResolution_Text
        {
            get { return Get("groupResolution_Text", "Detector-resolution adaptation"); }
        }

        public static string labelResolution_Text
        {
            get { return Get("labelResolution_Text", "R, % at 662 keV"); }
        }

        public static string buttonFromSpectrum_Text
        {
            get { return Get("buttonFromSpectrum_Text", "from spectrum"); }
        }

        public static string labelCriterion_Text
        {
            get { return Get("labelCriterion_Text", "criterion"); }
        }

        public static string labelFactor_Text
        {
            get { return Get("labelFactor_Text", "× FWHM"); }
        }

        public static string buttonMerge_Text
        {
            get { return Get("buttonMerge_Text", "Merge close lines"); }
        }

        public static string buttonUnmerge_Text
        {
            get { return Get("buttonUnmerge_Text", "Restore originals"); }
        }

        public static string mergeInfoFormat
        {
            get { return Get("mergeInfoFormat", "threshold {0:0.##}·FWHM: lines merge closer than {1:0.#} keV at 100, {2:0.#} at 662, {3:0.#} at 1500"); }
        }

        public static string criterionSparrow
        {
            get { return Get("criterionSparrow", "Sparrow limit — ROI markers (0.85·FWHM)"); }
        }

        public static string criterionMeasured
        {
            get { return Get("criterionMeasured", "measured optimum — set composition (0.7·FWHM)"); }
        }

        public static string criterionAnchored
        {
            get { return Get("criterionAnchored", "anchored set — library fit (0.25·FWHM)"); }
        }

        public static string criterionManual
        {
            get { return Get("criterionManual", "manual"); }
        }

        public static string groupFilters_Text
        {
            get { return Get("groupFilters_Text", "Filters and selection"); }
        }

        public static string checkIntensity_Text
        {
            get { return Get("checkIntensity_Text", "intensity ≥, %"); }
        }

        public static string intensityRelative
        {
            get { return Get("intensityRelative", "relative (within nuclide, max = 100)"); }
        }

        public static string intensityAbsolute
        {
            get { return Get("intensityAbsolute", "absolute (per decay)"); }
        }

        public static string checkEnergy_Text
        {
            get { return Get("checkEnergy_Text", "energy, keV"); }
        }

        public static string checkHalfLife_Text
        {
            get { return Get("checkHalfLife_Text", "T½"); }
        }

        public static string buttonSelectAll_Text
        {
            get { return Get("buttonSelectAll_Text", "✓ select all visible"); }
        }

        public static string buttonSelectNone_Text
        {
            get { return Get("buttonSelectNone_Text", "✗ deselect all visible"); }
        }

        public static string labelTopN_Text
        {
            get { return Get("labelTopN_Text", "top-N by I per nuclide"); }
        }

        public static string buttonSelectTop_Text
        {
            get { return Get("buttonSelectTop_Text", "Select top-N"); }
        }

        public static string checkHideUnselected_Text
        {
            get { return Get("checkHideUnselected_Text", "hide unselected"); }
        }

        public static string labelTypes_Text
        {
            get { return Get("labelTypes_Text", "Line types"); }
        }

        public static string checkTypeXray_Text
        {
            get { return Get("checkTypeXray_Text", "X (decay)"); }
        }

        public static string checkTypeXrf_Text
        {
            get { return Get("checkTypeXrf_Text", "XRF"); }
        }

        public static string checkTypeSecondary_Text
        {
            get { return Get("checkTypeSecondary_Text", "secondary"); }
        }

        public static string checkEquilibrium_Text
        {
            get { return Get("checkEquilibrium_Text", "series equilibrium (intensities per parent decay)"); }
        }

        public static string unitSeconds
        {
            get { return Get("unitSeconds", "s"); }
        }

        public static string unitHours
        {
            get { return Get("unitHours", "h"); }
        }

        public static string unitDays
        {
            get { return Get("unitDays", "d"); }
        }

        public static string unitYears
        {
            get { return Get("unitYears", "y"); }
        }

        public static string hlSeconds
        {
            get { return Get("hlSeconds", "s"); }
        }

        public static string hlMinutes
        {
            get { return Get("hlMinutes", "min"); }
        }

        public static string hlHours
        {
            get { return Get("hlHours", "h"); }
        }

        public static string hlDays
        {
            get { return Get("hlDays", "d"); }
        }

        public static string hlYears
        {
            get { return Get("hlYears", "y"); }
        }

        public static string columnLineName_Text
        {
            get { return Get("columnLineName_Text", "Nuclide"); }
        }

        public static string columnLineEnergy_Text
        {
            get { return Get("columnLineEnergy_Text", "E, keV"); }
        }

        public static string columnLineIntensity_Text
        {
            get { return Get("columnLineIntensity_Text", "I, %"); }
        }

        public static string columnLineRelative_Text
        {
            get { return Get("columnLineRelative_Text", "I rel., %"); }
        }

        public static string columnLineHalfLife_Text
        {
            get { return Get("columnLineHalfLife_Text", "T½"); }
        }

        public static string columnLineType_Text
        {
            get { return Get("columnLineType_Text", "Type"); }
        }

        public static string lineTypeXrf
        {
            get { return Get("lineTypeXrf", "XRF"); }
        }

        public static string lineTypeSecondary
        {
            get { return Get("lineTypeSecondary", "sec"); }
        }

        public static string groupSecondary_Text
        {
            get { return Get("groupSecondary_Text", "Secondary peaks (computed from selected γ lines)"); }
        }

        public static string labelSecondaryMin_Text
        {
            get { return Get("labelSecondaryMin_Text", "for γ lines with I ≥, %"); }
        }

        public static string checkSecBackscatter_Text
        {
            get { return Get("checkSecBackscatter_Text", "backscatter (BS)"); }
        }

        public static string checkSecComptonEdge_Text
        {
            get { return Get("checkSecComptonEdge_Text", "Compton edge (CE)"); }
        }

        public static string checkSecSingleEscape_Text
        {
            get { return Get("checkSecSingleEscape_Text", "escape 511 (SE)"); }
        }

        public static string checkSecDoubleEscape_Text
        {
            get { return Get("checkSecDoubleEscape_Text", "escape 1022 (DE)"); }
        }

        public static string checkSecIodine_Text
        {
            get { return Get("checkSecIodine_Text", "I-K escape (NaI, −28.6)"); }
        }

        public static string checkSecAnnihilation_Text
        {
            get { return Get("checkSecAnnihilation_Text", "annihilation 511"); }
        }

        public static string checkSecSum_Text
        {
            get { return Get("checkSecSum_Text", "cascade sum (E1+E2)"); }
        }

        public static string checkSecPileUp_Text
        {
            get { return Get("checkSecPileUp_Text", "pile-up 2×E"); }
        }

        public static string buttonGenerateSecondary_Text
        {
            get { return Get("buttonGenerateSecondary_Text", "Generate"); }
        }

        public static string statusMerged
        {
            get { return Get("statusMerged", "merged groups: {0}, lines absorbed: {1}"); }
        }

        public static string statusRoiCreated
        {
            get { return Get("statusRoiCreated", "ROI configuration «{0}» created: {1} regions"); }
        }

        public static string tipConfigName
        {
            get { return Get("tipConfigName", "The name becomes the file name under config\\ROI; a matching name overwrites that file."); }
        }

        public static string tipSetName
        {
            get { return Get("tipSetName", "Name of the set in the nuclide library; a duplicate name is confirmed before saving."); }
        }

        public static string tipFullSet
        {
            get { return Get("tipFullSet", "Applies to the SET only: the ROI configuration is always built from the table."); }
        }

        public static string tipAnchorManual
        {
            get { return Get("tipAnchorManual", "Anchor chosen by hand. Disabled with the full set: there the anchors are picked automatically."); }
        }

        public static string tipAnchorCount
        {
            get { return Get("tipAnchorCount", "How many lines to mark as anchors when they are picked automatically."); }
        }

        public static string statusRoiNotMeasurable
        {
            get { return Get("statusRoiNotMeasurable", "{0} of them do not measure area (markers: no zone, no Bq/cps coefficient)"); }
        }

        public static string statusSetCreated
        {
            get { return Get("statusSetCreated", "set «{0}» added to the library: {1} lines, {2} anchor(s)"); }
        }

        public static string secondaryFormat
        {
            get { return Get("secondaryFormat", "secondary markers added: {0}"); }
        }

        public static string annihilationLabel
        {
            get { return Get("annihilationLabel", "Annihilation 511"); }
        }

        public static string groupNear_Text
        {
            get { return Get("groupNear_Text", "Nearby-line search (whole database — who else emits here)"); }
        }

        public static string labelNearEnergy_Text
        {
            get { return Get("labelNearEnergy_Text", "energy, keV"); }
        }

        public static string labelNearWindow_Text
        {
            get { return Get("labelNearWindow_Text", "± window"); }
        }

        public static string labelNearIntensity_Text
        {
            get { return Get("labelNearIntensity_Text", "I ≥, %"); }
        }

        public static string labelNearHalfLife_Text
        {
            get { return Get("labelNearHalfLife_Text", "T½ ≥"); }
        }

        public static string buttonNearSearch_Text
        {
            get { return Get("buttonNearSearch_Text", "Search"); }
        }

        public static string buttonNearAdd_Text
        {
            get { return Get("buttonNearAdd_Text", "+ add"); }
        }

        public static string columnNearDelta_Text
        {
            get { return Get("columnNearDelta_Text", "ΔE"); }
        }

        public static string nearAdded
        {
            get { return Get("nearAdded", "added"); }
        }

        public static string nearMoreFormat
        {
            get { return Get("nearMoreFormat", "showing the first {0} of {1}"); }
        }

        public static string nearEmptyFormat
        {
            get { return Get("nearEmptyFormat", "nothing found within {0} ± {1} keV"); }
        }

        public static string groupStyle_Text
        {
            get { return Get("groupStyle_Text", "ROI styling"); }
        }

        public static string labelStyle_Text
        {
            get { return Get("labelStyle_Text", "mode"); }
        }

        public static string labelWidth_Text
        {
            get { return Get("labelWidth_Text", "zone width"); }
        }

        public static string labelColors_Text
        {
            get { return Get("labelColors_Text", "Colours"); }
        }

        public static string buttonColorByChain_Text
        {
            get { return Get("buttonColorByChain_Text", "by chain"); }
        }

        public static string buttonColorByNuclide_Text
        {
            get { return Get("buttonColorByNuclide_Text", "by nuclide"); }
        }

        public static string roiStyleMarkers
        {
            get { return Get("roiStyleMarkers", "marker lines (height ∝ I, no zones)"); }
        }

        public static string roiStyleZones
        {
            get { return Get("roiStyleZones", "zones (limits around the peak)"); }
        }

        public static string roiStyleBoth
        {
            get { return Get("roiStyleBoth", "zones + intensity markers"); }
        }

        public static string widthModePercent
        {
            get { return Get("widthModePercent", "% of energy (BecqMoni style)"); }
        }

        public static string widthModeFwhm
        {
            get { return Get("widthModeFwhm", "k × FWHM (scintillator)"); }
        }

        public static string groupExport_Text
        {
            get { return Get("groupExport_Text", "Create"); }
        }

        public static string labelConfigName_Text
        {
            get { return Get("labelConfigName_Text", "ROI configuration name"); }
        }

        public static string buttonCreateRoi_Text
        {
            get { return Get("buttonCreateRoi_Text", "Create ROI configuration"); }
        }

        public static string buttonPreview_Text
        {
            get { return Get("buttonPreview_Text", "Preview"); }
        }

        public static string labelSetName_Text
        {
            get { return Get("labelSetName_Text", "set name (NuclideSet)"); }
        }

        public static string textSetName_Text
        {
            get { return Get("textSetName_Text", "IAEA set"); }
        }

        public static string labelAnchor_Text
        {
            get { return Get("labelAnchor_Text", "anchor line"); }
        }

        public static string buttonCreateSet_Text
        {
            get { return Get("buttonCreateSet_Text", "Add set to the library"); }
        }

        public static string checkFullSet_Text
        {
            get { return Get("checkFullSet_Text", "recommended composition (0.7·FWHM, ≥1 %)"); }
        }

        public static string labelAnchorCount_Text
        {
            get { return Get("labelAnchorCount_Text", "anchor lines"); }
        }

        public static string labelIssues_Text
        {
            get { return Get("labelIssues_Text", "Data check:"); }
        }

        public static string previewEmpty
        {
            get { return Get("previewEmpty", "no lines selected"); }
        }

        public static string anchorAuto
        {
            get { return Get("anchorAuto", "auto — {0} {1}"); }
        }

        public static string issuePrefixRoi
        {
            get { return Get("issuePrefixRoi", "ROI"); }
        }

        public static string issuePrefixSet
        {
            get { return Get("issuePrefixSet", "SET"); }
        }

        public static string issueNone
        {
            get { return Get("issueNone", "no issues"); }
        }

        public static string issueEqualEnergies
        {
            get { return Get("issueEqualEnergies", "equal energies: “{0}” and “{1}” ({2} / {3} keV) — the amplitude fit degenerates here"); }
        }

        public static string issueZeroYield
        {
            get { return Get("issueZeroYield", "zero yield: “{0}” ({1} keV)"); }
        }

        public static string issueAnchorIsXrf
        {
            get { return Get("issueAnchorIsXrf", "the anchor “{0}” ({1} keV) is a characteristic X-ray of a material, not a decay line: the fit would rest on a line whose position or intensity is nominal"); }
        }

        public static string issueAnchorIsSecondary
        {
            get { return Get("issueAnchorIsSecondary", "the anchor “{0}” ({1} keV) is a computed secondary marker, not a decay line: the fit would rest on a line whose position or intensity is nominal"); }
        }

        public static string issueNoAnchor
        {
            get { return Get("issueNoAnchor", "no anchor line: the set holds no decay line at all (XRF and secondary markers cannot be anchors) — the library fit does not start without one"); }
        }

        public static string issueAnchorIsXray
        {
            get { return Get("issueAnchorIsXray", "the anchor is the X-ray line “{0}” ({1} keV): a γ line is a firmer footing for the fit"); }
        }

        public static string issueMixedChains
        {
            get { return Get("issueMixedChains", "the set mixes decay series ({0}): the anchor gate is common to the whole set, so one matched anchor switches the others on as well and they yield false identifications"); }
        }

        public static string issueZonesOverlap
        {
            get { return Get("issueZonesOverlap", "zones overlap: “{0}” [{1}–{2}] and “{3}” [{4}–{5}]"); }
        }

        public static string confirmTitle
        {
            get { return Get("confirmTitle", "ROI and nuclide set builder"); }
        }

        public static string confirmRoiOverwrite
        {
            get { return Get("confirmRoiOverwrite", "A configuration named “{0}” already exists — its file will be overwritten. Continue?"); }
        }

        public static string confirmSetDuplicate
        {
            get { return Get("confirmSetDuplicate", "The library already holds a set named “{0}”. Add another one with the same name?"); }
        }

        public static string noLinesSelected
        {
            get { return Get("noLinesSelected", "No lines selected."); }
        }

        public static string noResolutionFromSpectrum
        {
            get { return Get("noResolutionFromSpectrum", "The resolution could not be taken from the active spectrum."); }
        }

        public static string confirmErrorsHead
        {
            get { return Get("confirmErrorsHead", "The set cannot be saved — the data check found errors:"); }
        }

        public static string confirmIssuesHead
        {
            get { return Get("confirmIssuesHead", "The data check found issues:"); }
        }

        public static string confirmErrorsTail
        {
            get { return Get("confirmErrorsTail", "Two lines at the same energy make the amplitude fit degenerate, and zero intensity drops a line out of the chain coupling."); }
        }

        public static string confirmSaveAnyway
        {
            get { return Get("confirmSaveAnyway", "Save anyway?"); }
        }

        public static string helpTitle
        {
            get { return Get("helpTitle", "Help: ROI and nuclide set builder"); }
        }

        public static string helpSourcesArrow
        {
            get { return Get("helpSourcesArrow", "   →   "); }
        }

    }
}