// Standalone UCD Ingestor - Populates ucd schema from UCD XML
// SEPARATE from Hartonomous - this is a data source

#include <fstream>
#include <iostream>
#include <string>
#include <sstream>
#include <pqxx/pqxx>

std::string get_attr(const std::string& line, const char* name) {
    std::string search = std::string(name) + "=\"";
    size_t start = line.find(search);
    if (start == std::string::npos) return "";
    start += search.length();
    size_t end = line.find('"', start);
    if (end == std::string::npos) return "";
    return line.substr(start, end - start);
}

std::string escape(const std::string& s) {
    std::string r;
    for (char c : s) {
        if (c == '\'') r += "''";
        else if (c == '\\') r += "\\\\";
        else r += c;
    }
    return r;
}

std::string quote(const std::string& s) {
    if (s.empty()) return "NULL";
    return "'" + escape(s) + "'";
}

std::string bool_val(const std::string& line, const char* attr) {
    return get_attr(line, attr) == "Y" ? "TRUE" : "FALSE";
}

int main(int argc, char** argv) {
    std::string xml_path = "data/ucd.all.flat.xml";
    std::string conn_str = "dbname=postgres";

    if (argc > 1) xml_path = argv[1];
    if (argc > 2) conn_str = argv[2];

    std::ifstream file(xml_path);
    if (!file) {
        std::cerr << "Cannot open: " << xml_path << "\n";
        return 1;
    }

    pqxx::connection conn(conn_str);
    pqxx::work txn(conn);

    std::cout << "Parsing " << xml_path << "...\n";

    std::string line;
    size_t count = 0;
    std::ostringstream batch;
    size_t batch_count = 0;

    while (std::getline(file, line)) {
        if (line.find("<char ") == std::string::npos) continue;

        std::string cp_str = get_attr(line, "cp");
        if (cp_str.empty()) continue;

        int cp = std::stoi(cp_str, nullptr, 16);

        if (batch_count == 0) {
            batch.str("");
            batch << "INSERT INTO ucd.codepoints (cp,name,name1,gc,ccc,sc,scx,blk,age,"
                  << "dt,dm,uc,lc,tc,suc,slc,stc,scf,cf,nt,nv,bc,bidi_m,bmg,bidi_c,"
                  << "bpt,bpb,jt,jg,join_c,ea,lb,wb,sb,gcb,insc,inpc,vo,hst,jsn,"
                  << "alpha,upper,lower,cased,math,hex,ahex,ideo,uideo,radical,"
                  << "dash,wspace,qmark,term,sterm,dia,ext,sd,dep,di,vs,nchar,"
                  << "pat_ws,pat_syn,gr_base,gr_ext,ids,idc,xids,xidc,ce,comp_ex,"
                  << "cwl,cwu,cwt,cwcf,cwcm,cwkcf,emoji,epres,emod,ebase,ecomp,extpict,"
                  << "pcm,ri,nfc_qc,nfd_qc,nfkc_qc,nfkd_qc) VALUES ";
        } else {
            batch << ",";
        }

        std::string ccc = get_attr(line, "ccc");

        batch << "(" << cp << "," << quote(get_attr(line, "na")) << ","
              << quote(get_attr(line, "na1")) << "," << quote(get_attr(line, "gc")) << ","
              << (ccc.empty() ? "0" : ccc) << "," << quote(get_attr(line, "sc")) << ","
              << quote(get_attr(line, "scx")) << "," << quote(get_attr(line, "blk")) << ","
              << quote(get_attr(line, "age")) << "," << quote(get_attr(line, "dt")) << ","
              << quote(get_attr(line, "dm")) << "," << quote(get_attr(line, "uc")) << ","
              << quote(get_attr(line, "lc")) << "," << quote(get_attr(line, "tc")) << ","
              << quote(get_attr(line, "suc")) << "," << quote(get_attr(line, "slc")) << ","
              << quote(get_attr(line, "stc")) << "," << quote(get_attr(line, "scf")) << ","
              << quote(get_attr(line, "cf")) << "," << quote(get_attr(line, "nt")) << ","
              << quote(get_attr(line, "nv")) << "," << quote(get_attr(line, "bc")) << ","
              << bool_val(line, "Bidi_M") << "," << quote(get_attr(line, "bmg")) << ","
              << bool_val(line, "Bidi_C") << "," << quote(get_attr(line, "bpt")) << ","
              << quote(get_attr(line, "bpb")) << "," << quote(get_attr(line, "jt")) << ","
              << quote(get_attr(line, "jg")) << "," << bool_val(line, "Join_C") << ","
              << quote(get_attr(line, "ea")) << "," << quote(get_attr(line, "lb")) << ","
              << quote(get_attr(line, "WB")) << "," << quote(get_attr(line, "SB")) << ","
              << quote(get_attr(line, "GCB")) << "," << quote(get_attr(line, "InSC")) << ","
              << quote(get_attr(line, "InPC")) << "," << quote(get_attr(line, "vo")) << ","
              << quote(get_attr(line, "hst")) << "," << quote(get_attr(line, "JSN")) << ","
              << bool_val(line, "Alpha") << "," << bool_val(line, "Upper") << ","
              << bool_val(line, "Lower") << "," << bool_val(line, "Cased") << ","
              << bool_val(line, "Math") << "," << bool_val(line, "Hex") << ","
              << bool_val(line, "AHex") << "," << bool_val(line, "Ideo") << ","
              << bool_val(line, "UIdeo") << "," << bool_val(line, "Radical") << ","
              << bool_val(line, "Dash") << "," << bool_val(line, "WSpace") << ","
              << bool_val(line, "QMark") << "," << bool_val(line, "Term") << ","
              << bool_val(line, "STerm") << "," << bool_val(line, "Dia") << ","
              << bool_val(line, "Ext") << "," << bool_val(line, "SD") << ","
              << bool_val(line, "Dep") << "," << bool_val(line, "DI") << ","
              << bool_val(line, "VS") << "," << bool_val(line, "NChar") << ","
              << bool_val(line, "Pat_WS") << "," << bool_val(line, "Pat_Syn") << ","
              << bool_val(line, "Gr_Base") << "," << bool_val(line, "Gr_Ext") << ","
              << bool_val(line, "IDS") << "," << bool_val(line, "IDC") << ","
              << bool_val(line, "XIDS") << "," << bool_val(line, "XIDC") << ","
              << bool_val(line, "CE") << "," << bool_val(line, "Comp_Ex") << ","
              << bool_val(line, "CWL") << "," << bool_val(line, "CWU") << ","
              << bool_val(line, "CWT") << "," << bool_val(line, "CWCF") << ","
              << bool_val(line, "CWCM") << "," << bool_val(line, "CWKCF") << ","
              << bool_val(line, "Emoji") << "," << bool_val(line, "EPres") << ","
              << bool_val(line, "EMod") << "," << bool_val(line, "EBase") << ","
              << bool_val(line, "EComp") << "," << bool_val(line, "ExtPict") << ","
              << bool_val(line, "PCM") << "," << bool_val(line, "RI") << ","
              << quote(get_attr(line, "NFC_QC")) << "," << quote(get_attr(line, "NFD_QC")) << ","
              << quote(get_attr(line, "NFKC_QC")) << "," << quote(get_attr(line, "NFKD_QC")) << ")";

        batch_count++;
        count++;

        if (batch_count >= 5000) {
            batch << " ON CONFLICT (cp) DO NOTHING";
            txn.exec(batch.str());
            batch_count = 0;
            std::cout << "\r  " << count << " codepoints..." << std::flush;
        }
    }

    if (batch_count > 0) {
        batch << " ON CONFLICT (cp) DO NOTHING";
        txn.exec(batch.str());
    }

    txn.commit();
    std::cout << "\nIngested " << count << " codepoints to ucd.codepoints\n";
    return 0;
}
