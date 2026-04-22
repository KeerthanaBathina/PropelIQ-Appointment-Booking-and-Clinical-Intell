/**
 * ClinicalDataTabs — US_043 SCR-013 tabbed clinical data panel (UXR-404).
 *
 * Four MUI Tabs (Medications, Diagnoses, Procedures, Allergies), each backed by
 * a DataPointTable for its category. Tab labels include a count badge.
 *
 * Category icons (UXR-404):
 *   Medication  — MedicalServicesIcon  (green)
 *   Diagnosis   — LocalHospitalIcon    (blue)
 *   Procedure   — HealingIcon          (purple)
 *   Allergy     — WarningAmberIcon     (orange)
 *
 * Row click calls onDataPointClick to open the SourceCitationPanel (AC-3).
 * Loading state shows skeleton rows in the active tab's DataPointTable.
 *
 * Empty state per tab shows an informational message if the category list is empty.
 */

import { useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import HealingIcon from '@mui/icons-material/Healing';
import LocalHospitalIcon from '@mui/icons-material/LocalHospital';
import MedicalServicesIcon from '@mui/icons-material/MedicalServices';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';

import DataPointTable from './DataPointTable';
import type { ProfileDataPointDto } from '@/hooks/usePatientProfile';

// ─── Tab definitions ──────────────────────────────────────────────────────────

type DataTab = 'Medication' | 'Diagnosis' | 'Procedure' | 'Allergy';

interface TabDef {
  key: DataTab;
  label: string;
  icon: React.ReactElement;
  iconColor: string;
}

const TAB_DEFS: TabDef[] = [
  {
    key: 'Medication',
    label: 'Medications',
    icon: <MedicalServicesIcon fontSize="small" />,
    iconColor: '#2E7D32', // success.main — green
  },
  {
    key: 'Diagnosis',
    label: 'Diagnoses',
    icon: <LocalHospitalIcon fontSize="small" />,
    iconColor: '#1976D2', // primary.main — blue
  },
  {
    key: 'Procedure',
    label: 'Procedures',
    icon: <HealingIcon fontSize="small" />,
    iconColor: '#7B1FA2', // secondary.main — purple
  },
  {
    key: 'Allergy',
    label: 'Allergies',
    icon: <WarningAmberIcon fontSize="small" />,
    iconColor: '#E65100', // warning.dark — orange
  },
];

// ─── Accessible tab panel wrapper ─────────────────────────────────────────────

interface TabPanelProps {
  children: React.ReactNode;
  active: boolean;
  tabKey: string;
}

function TabPanel({ children, active, tabKey }: TabPanelProps) {
  return (
    <div
      role="tabpanel"
      hidden={!active}
      id={`panel-${tabKey.toLowerCase()}`}
      aria-labelledby={`tab-${tabKey.toLowerCase()}`}
    >
      {active && <Box>{children}</Box>}
    </div>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface ClinicalDataTabsProps {
  medications: ProfileDataPointDto[];
  diagnoses: ProfileDataPointDto[];
  procedures: ProfileDataPointDto[];
  allergies: ProfileDataPointDto[];
  isLoading: boolean;
  /** Called when a row is clicked; surfaces extractedDataId to page level for citation panel. */
  onDataPointClick: (extractedDataId: string) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ClinicalDataTabs({
  medications,
  diagnoses,
  procedures,
  allergies,
  isLoading,
  onDataPointClick,
}: ClinicalDataTabsProps) {
  const [activeTab, setActiveTab] = useState<DataTab>('Medication');

  const dataMap: Record<DataTab, ProfileDataPointDto[]> = {
    Medication: medications,
    Diagnosis:  diagnoses,
    Procedure:  procedures,
    Allergy:    allergies,
  };

  return (
    <Paper variant="outlined" sx={{ borderRadius: 2, overflow: 'hidden' }}>
      {/* Tab bar */}
      <Tabs
        value={activeTab}
        onChange={(_, newVal: DataTab) => setActiveTab(newVal)}
        aria-label="Clinical data categories"
        variant="scrollable"
        scrollButtons="auto"
        sx={{ borderBottom: 1, borderColor: 'divider', bgcolor: 'grey.50' }}
      >
        {TAB_DEFS.map((def) => {
          const count = dataMap[def.key].length;
          return (
            <Tab
              key={def.key}
              value={def.key}
              id={`tab-${def.key.toLowerCase()}`}
              aria-controls={`panel-${def.key.toLowerCase()}`}
              label={
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                  <Box sx={{ color: def.iconColor, display: 'flex', alignItems: 'center' }} aria-hidden="true">
                    {def.icon}
                  </Box>
                  <span>
                    {def.label}
                    {!isLoading && (
                      <Typography
                        component="span"
                        variant="caption"
                        sx={{ ml: 0.5, color: 'text.secondary' }}
                      >
                        ({count})
                      </Typography>
                    )}
                  </span>
                </Box>
              }
              sx={{ textTransform: 'none', minWidth: 130 }}
            />
          );
        })}
      </Tabs>

      {/* Panels */}
      {TAB_DEFS.map((def) => {
        const rows = dataMap[def.key];
        return (
          <TabPanel key={def.key} active={activeTab === def.key} tabKey={def.key}>
            {!isLoading && rows.length === 0 ? (
              <Box
                sx={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  py: 6,
                  color: 'text.disabled',
                  gap: 1,
                }}
                role="status"
                aria-label={`No ${def.label.toLowerCase()} data`}
              >
                <Box sx={{ color: def.iconColor, opacity: 0.4, fontSize: 48 }} aria-hidden="true">
                  {def.icon}
                </Box>
                <Typography variant="body2">
                  No {def.label.toLowerCase()} data found in this patient's profile.
                </Typography>
              </Box>
            ) : (
              <DataPointTable
                dataType={def.key}
                rows={rows}
                isLoading={isLoading}
                onRowClick={onDataPointClick}
              />
            )}
          </TabPanel>
        );
      })}
    </Paper>
  );
}
