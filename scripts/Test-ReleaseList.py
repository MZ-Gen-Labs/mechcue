"""Verify release chronology with seconds, offsets, multiple pages and HTML escaping."""
import json,pathlib,subprocess,tempfile
from html.parser import HTMLParser
root=pathlib.Path(__file__).resolve().parents[1]
class Rows(HTMLParser):
    def __init__(self):super().__init__();self.section=None;self.rows={};self.in_row=False;self.row=''
    def handle_starttag(self,tag,attrs):
        if tag=='section':self.section=dict(attrs)['id'];self.rows[self.section]=[]
        if tag=='tr':self.in_row=True;self.row=''
    def handle_endtag(self,tag):
        if tag=='tr' and self.in_row:
            self.rows[self.section].append(self.row);self.in_row=False
    def handle_data(self,data):
        if self.in_row:self.row+=data

def release(tag,time,name=None):
    return {'name':name or tag,'tagName':tag,'createdAt':time,'isDraft':True,'isPrerelease':True,'url':'https://github.com/MZ-Gen-Labs/mechcue/releases/tag/'+tag,'releaseAssets':{'nodes':[{'name':'MechCue-AddIn-Setup.exe','downloadUrl':'https://github.com/MZ-Gen-Labs/mechcue/releases/download/'+tag+'/Setup.exe'}]}}
def page(rows):return {'data':{'repository':{'viewerPermission':'ADMIN','releases':{'nodes':rows}}}}
(root/'artifacts').mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='release-list-',dir=root/'artifacts') as tmp:
    tmp=pathlib.Path(tmp);source=tmp/'pages.json';output=tmp/'index.html'
    source.write_text(json.dumps([
        page([release('v0.2.0-alpha.10','2026-10-01T10:00:02Z'),release('v0.10.0-alpha.1','2026-09-30T10:00:00Z')]),
        page([release('v0.2.0-alpha.9','2026-10-01T10:00:01Z'),release('v0.2.0-alpha.2','2026-10-02T00:00:00+14:00','<script>alert("test")</script>')])]),encoding='utf-8')
    rendered=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(root/'scripts/Open-ReleaseList.ps1'),'-InputJson',str(source),'-OutputPath',str(output),'-NoOpen'],capture_output=True)
    assert rendered.returncode==0,rendered.stderr.decode("utf-8",errors="replace")
    html=output.read_text(encoding='utf-8');p=Rows();p.feed(html)
    assert 'v0.10.0-alpha.1' in p.rows['group-0'][-1],p.rows
    rows=p.rows['group-1'][1:]
    assert '<script>alert("test")</script>' in rows[0],rows # UTC instant, not its local calendar date
    assert 'v0.2.0-alpha.9' in rows[1] and 'v0.2.0-alpha.10' in rows[2],rows
    assert '最新' in rows[2] and all('最新' not in r for r in rows[:2]),rows
    assert '<script>alert("test")</script>' not in html and '&lt;script&gt;' in html
    assert '2026-10-01 19:00:01' in html and '2026-10-01 19:00:02' in html
    assert html.count('>アドイン版</a>')==4
    assert 'GitHubのログイン権限で取得したDraftを含みます' in html
    before=output.read_bytes();source.write_text(json.dumps([{'errors':[{'message':'denied'}]}]),encoding='utf-8')
    failed=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(root/'scripts/Open-ReleaseList.ps1'),'-InputJson',str(source),'-OutputPath',str(output),'-NoOpen'],capture_output=True)
    assert failed.returncode!=0 and output.read_bytes()==before
print('PASS: second-level UTC ordering, pagination, numeric groups, latest marker, Japanese time, escaped titles, asset links and failed refresh preservation')
