# ConvertAPLToMGF
Convert Andromeda peak-lists to Mascot generic format including feature information

This program allows you to convert MaxQuant APL peaklist files to compatible MGF peaklist files. Ensure that the APL files are 
generated accurately by running MaxQuant and Andromeda against a suitable sequence database reflecting the sample under study
i.e., do NOT limit the search to contaminants only in order to speed up the search.

Drag-and-drop APL files from Windows Explorer onto the empty window or choose Add Files (include all apl files including secpep).
Choose Select to indicate the location of Maxquant's TXT folder (evidence, allpeptides, msmsScans etc.).
Demixing of chimeric spectra is supported if the "Use original peaklist for secondary peptides" is checked (default).
The peaklists will be written to mgf files using the original MS raw file name if "Write to original MS acquired file name"
is checked (default). 
Select the fragment ions type appropriate for your MS instrument (if in doubt e-mail eugkapp at gmail).
Click Process to convert APL files to MGF. Files will be written to the same location.
Select Cancel in order to interrupt the conversion process.
Select Quit to close the application.
Enjoy.
