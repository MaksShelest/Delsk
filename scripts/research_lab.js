/* Research presentation only. Author: Шелестов Максим Евгеньевич. */
'use strict';
const $=id=>document.getElementById(id), clone=x=>JSON.parse(JSON.stringify(x));
const esc=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const labels={queued:'В очереди',running:'Выполняется',succeeded:'Готово',failed:'Ошибка',interrupted:'Прерван',cancelled:'Остановлен',paused:'Пауза',completed:'Готово',exporting:'Сохранение файлов','waiting-model':'Ожидание модели',voting:'Голосование'};
const money=n=>Number(n/100).toLocaleString('ru-RU',{maximumFractionDigits:2});
const bytes=n=>n>1048576?(n/1048576).toFixed(1)+' МБ':n>1024?(n/1024).toFixed(1)+' КБ':n+' Б';
let catalog,state={series:[]},draft,economy,scenario,profiles=[],members=[],customMembers=false,page='catalog',selectedId=null,selected=new Set(),detailTab='runs',runsPage=0,worldPlayback=null,filePath='',fileCursor=0,journalCursor=0;
function alertError(e){$('error').textContent=String(e.message??e);$('error').hidden=false;}
function notice(text){$('notice').textContent=text;$('notice').hidden=false;setTimeout(()=>$('notice').hidden=true,7000);}
async function api(path,data){
 const r=await fetch(path,{method:data===undefined?'GET':'POST',headers:{'X-Lab-Token':token,'Content-Type':'application/json'},body:data===undefined?undefined:JSON.stringify(data)});
 const result=await r.json();if(!r.ok)throw Error(result.error??r.status);return result;
}
const attemptParams=()=>({id:selectedId,job:$('job').value,attempt:$('attempt').value});
const query=(path,args)=>path+'?'+new URLSearchParams(args);
function currentSeries(){return state.series.find(s=>s.id===selectedId);}
function currentJob(){return currentSeries()?.jobs.find(j=>j.id===$('job').value);}
function prefix(){return 'runs/'+$('job').value+'/'+$('attempt').value+'/';}
function bind(id,fn){$(id).addEventListener('click',async e=>{try{$('error').hidden=true;await fn(e);}catch(error){alertError(error);}});}
function table(headers,rows){return '<div class="table-wrap"><table><thead><tr>'+headers.map(h=>'<th>'+esc(h)+'</th>').join('')+'</tr></thead><tbody>'+rows.map(r=>'<tr>'+r.map(v=>'<td>'+v+'</td>').join('')+'</tr>').join('')+'</tbody></table></div>';}
async function showPage(name){
 page=name;document.querySelectorAll('.page').forEach(p=>p.hidden=p.id!=='page-'+name);
 document.querySelectorAll('nav [data-page]').forEach(b=>b.classList.toggle('active',b.dataset.page===name));
 $('breadcrumb').textContent='Рабочее пространство / '+({catalog:'Исследования',new:'Новый опыт',detail:'Результаты',profiles:'Профили экономики',models:'Модели'}[name]??name);
 if(name!=='detail')stopPlayback();
 if(name==='catalog')renderCatalog();
 if(name==='new')syncPlan();
 if(name==='models'||name==='profiles')await loadProfiles();
 if(name==='detail')renderDetail();
}
document.addEventListener('click',e=>{
 const nav=e.target.closest('[data-page]');if(nav){e.preventDefault();showPage(nav.dataset.page).catch(alertError);}
 const queue=e.target.closest('[data-queue]');if(queue)api('/api/'+queue.dataset.queue,{}).then(refresh).catch(alertError);
 const control=e.target.closest('[data-control]');if(control){
  if(control.dataset.control==='cancelled'&&!confirm('Остановить текущий прогон? Частичные файлы сохранятся, результат не войдёт в статистику.'))return;
  api('/api/control',{id:selectedId,action:control.dataset.control}).then(refresh).catch(alertError);
 }
});
function tab(container,base,cls,name){
 document.querySelectorAll('#'+container+' button').forEach(b=>b.classList.toggle('active',b.dataset.tab===name));
 document.querySelectorAll('.'+cls).forEach(p=>p.hidden=p.id!==base+name);
}
$('setup-tabs').addEventListener('click',e=>{if(e.target.dataset.tab){tab('setup-tabs','setup-','setup-panel',e.target.dataset.tab);if(e.target.dataset.tab==='json')syncPlan();}});
$('detail-tabs').addEventListener('click',e=>{if(e.target.dataset.tab){detailTab=e.target.dataset.tab;tab('detail-tabs','detail-','detail-panel',detailTab);loadDetailTab().catch(alertError);}});
function integer(value,min,max,label){const n=Number(value);if(!/^[0-9]+$/.test(String(value))||!Number.isSafeInteger(n)||n<min||n>max)throw Error(label+': целое число '+min+'–'+max);return n;}
function parsePlan(text){
 const result=JSON.parse(text);let at=0;
 const whitespace=()=>{while(at<text.length&&/\s/.test(text[at]))at++;};
 function string(){
  const start=at++;while(text[at]!=='"'){if(text[at]==='\\')at+=2;else at++;}
  at++;return JSON.parse(text.slice(start,at));
 }
 function value(){
  whitespace();
  if(text[at]==='{'){
   at++;whitespace();const keys=new Set();
   while(text[at]!=='}'){
    const key=string();if(keys.has(key))throw Error('Повторяющийся ключ JSON: '+key);keys.add(key);
    whitespace();at++;value();whitespace();if(text[at]!==',')break;at++;whitespace();
   }at++;
  }else if(text[at]==='['){
   at++;whitespace();while(text[at]!==']'){value();whitespace();if(text[at]!==',')break;at++;}at++;
  }else if(text[at]==='"')string();
  else{
   const start=at;while(at<text.length&&!/[\s,}\]]/.test(text[at]))at++;
   const raw=text.slice(start,at),number=Number(raw);
   if(Number.isInteger(number)&&!Number.isSafeInteger(number))throw Error('JSON-число выходит за точность интерфейса: '+raw);
  }
 }
 value();return result;
}
function minor(value){
 if(!/^[0-9]+([.,][0-9]{1,2})?$/.test(value))throw Error('Деньги: не более двух знаков после запятой');
 const [whole,part='']=value.replace(',','.').split('.');const n=BigInt(whole)*100n+BigInt(part.padEnd(2,'0'));if(n>BigInt(Number.MAX_SAFE_INTEGER))throw Error('Слишком большая сумма');return Number(n);
}
function formPlan(){
 const result=clone(draft);
 result.id=$('experiment-name').value.trim();result.scenario=$('map').value;
 result.ticks=integer($('ticks').value,1,10000,'Тики');result.repetitions=integer($('repetitions').value,1,500,'Повторы');
 const seeds=$('seeds').value.split(',').map(s=>s.trim());if(seeds.some(s=>!s))throw Error('Укажите каждый seed без пустых значений');
 result.seeds=seeds.map(s=>integer(s,0,Number.MAX_SAFE_INTEGER,'Seed'));
 result.timeoutSeconds=integer($('timeout').value,1,900,'Лимит времени');result.mode=$('mode').value;
 if(economy)result.economyData=clone(economy);
 if(customMembers)result.participants=clone(members);
 else delete result.participants;

 if(result.mode==='hybrid'){
  const bindings={};let timeout=1000;
  document.querySelectorAll('[data-member-model]').forEach(select=>{
   const id=select.dataset.memberModel,profile=profiles.find(p=>p.id===select.value);
   bindings[id]=profile?profile.value.ollama:null;timeout=Math.max(timeout,profile?.value.runtime?.timeoutMilliseconds??1000);
  });
  if(Object.values(bindings).some(Boolean)){delete result.modelConfig;result.modelData={ollama:{endpoint:'http://127.0.0.1:11434/',model:'qwen3.5:4b'},policy:{minimumIntervalTicks:30,applyDelayTicks:2,decisionTtlTicks:2,budgetPeriodTicks:120,maximumRequestsPerPeriod:2},runtime:{queueCapacity:3,timeoutMilliseconds:timeout},bindings};}
  else if(!result.modelData&&!result.modelConfig)throw Error('Выберите профиль хотя бы одной реальной модели во вкладке «Участники»');
 }else {delete result.modelData;delete result.modelConfig;}
 return result;
}
function syncPlan(){try{const p=formPlan();$('plan').value=JSON.stringify(p,null,2);let count=p.seeds.length*p.repetitions;for(const f of p.factors)count*=f.values?.length??1;$('estimate').textContent='Запланировано прогонов: '+count+' · '+p.ticks+' тиков · '+(p.mode==='algorithmic'?'алгоритмы':'гибрид');}catch(e){$('estimate').textContent=e.message;}}
async function setPlan(plan){
 draft=clone(plan);$('experiment-name').value=plan.id;$('map').value=plan.scenario;$('ticks').value=plan.ticks;$('seeds').value=plan.seeds.join(', ');$('repetitions').value=plan.repetitions;$('timeout').value=plan.timeoutSeconds??180;$('mode').value=plan.mode;
 const c=await api(query('/api/config',{map:plan.scenario}));scenario=c.scenario;economy=clone(plan.economyData??c.economy);if(!plan.economyData)draft.economy=c.economyPath;
 customMembers=!!plan.participants;members=plan.participants?clone(plan.participants):scenario.participants.map((m,i)=>({id:m.id,cashMinor:economy.world.startingCashMinor,inventoryCapacity:economy.world.personalInventoryCapacity,position:m.position,preferredInput:m.publicPreferenceItem||(['grain','wood'][i%2]),difficulty:'Medium'}));
 $('npc-count').value=members.length-1;renderMembers();renderEconomy();renderFactors();syncPlan();
}
function renderMembers(){
 $('members').innerHTML=members.map((m,i)=>'<div class="member"><h3>'+(i?'NPC '+i:'Человек · пассивный слот')+'</h3><div class="member-grid">'+
 '<label>Деньги, монеты<input data-member="'+i+'" data-key="cashMinor" value="'+esc((m.cashMinor/100).toFixed(2))+'"></label>'+
 '<label>Вместимость<input type="number" data-member="'+i+'" data-key="inventoryCapacity" value="'+m.inventoryCapacity+'"></label>'+
 '<label>Старт X<input type="number" data-member="'+i+'" data-key="x" value="'+m.position.x+'"></label><label>Старт Z<input type="number" data-member="'+i+'" data-key="z" value="'+m.position.z+'"></label>'+
 '<label>Предпочтение<select data-member="'+i+'" data-key="preferredInput">'+[...new Set(scenario.resourceNodes.map(n=>n.itemId))].map(item=>'<option '+(item===m.preferredInput?'selected':'')+'>'+esc(item)+'</option>').join('')+'</select></label>'+
 '<label>Сложность<select data-member="'+i+'" data-key="difficulty">'+['Easy','Medium','Hard'].map(item=>'<option '+(item===(m.difficulty??'Medium')?'selected':'')+'>'+item+'</option>').join('')+'</select></label>'+
 (i?'<label>Управление<select data-member-model="'+m.id+'"><option value="">Алгоритм</option>'+profiles.filter(p=>p.kind==='model').map(p=>'<option value="'+p.id+'">'+esc(p.name)+'</option>').join('')+'</select></label>':'')+'</div></div>').join('');
}
$('members').addEventListener('change',e=>{try{if(e.target.dataset.memberModel){syncPlan();return;}if(e.target.dataset.member===undefined)return;
 const m=members[Number(e.target.dataset.member)],key=e.target.dataset.key,value=e.target.value;
 if(key==='cashMinor')m[key]=minor(value);
 else if(key==='x'||key==='z')m.position[key]=integer(value,0,(key==='x'?scenario.mapWidth:scenario.mapHeight)-1,'Старт');
 else if(key==='inventoryCapacity')m[key]=integer(value,1,1000000000,'Вместимость');else m[key]=value;
 customMembers=true;syncPlan();
 }catch(err){alertError(err);e.target.focus();}});
$('npc-count').addEventListener('change',()=>{
 try{
 const count=integer($('npc-count').value,1,15,'NPC')+1;if(count===members.length)return;const blocked=new Set(scenario.blockedCells.map(c=>c.x+','+c.z));
 while(members.length<count){
  const occupied=new Set(members.map(m=>m.position.x+','+m.position.z));let position;
  const base=scenario.capitalDemand?.terminal??members[0].position;
  const cells=[];for(let x=0;x<scenario.mapWidth;x++)for(let z=0;z<scenario.mapHeight;z++)if(!blocked.has(x+','+z)&&!occupied.has(x+','+z))cells.push({x,z});
  cells.sort((a,b)=>(Math.abs(a.x-base.x)+Math.abs(a.z-base.z))-(Math.abs(b.x-base.x)+Math.abs(b.z-base.z)));position=cells[0];
  if(!position)throw Error('Нет свободной стартовой клетки');members.push({id:members.length+1,cashMinor:economy.world.startingCashMinor,inventoryCapacity:economy.world.personalInventoryCapacity,position,preferredInput:members.length%2?'wood':'grain',difficulty:'Medium'});
 }
 members=members.slice(0,count);customMembers=true;renderMembers();syncPlan();
 }catch(e){alertError(e);}
});
function renderEconomy(){
 const needle=$('economy-search').value.toLowerCase();let output='';
 function fields(node,path){
  let html='';for(const [key,v] of Object.entries(node)){
   const full=path+'.'+key;if(Array.isArray(v)||v===null)continue;
   if(typeof v==='object'){html+=fields(v,full);continue;}
   if(!full.toLowerCase().includes(needle))continue;
   html+='<div class="economy-field"><label>'+esc(full)+(typeof v==='boolean'?'<select data-economy="'+esc(full)+'"><option value="true" '+(v?'selected':'')+'>true</option><option value="false" '+(!v?'selected':'')+'>false</option></select>':'<input data-economy="'+esc(full)+'" data-type="'+typeof v+'" value="'+esc(v)+'">')+'</label></div>';
  }return html;
 }
 for(const key of ['world','npc']){const html=fields(economy[key],key);if(html)output+='<details class="economy-section" open><summary>'+esc(key==='world'?'Мир и хозяйство':'Поведение NPC')+'</summary><div class="form-grid">'+html+'</div></details>';}
 $('economy-fields').innerHTML=output;
}
$('economy-search').addEventListener('input',renderEconomy);
$('economy-fields').addEventListener('change',e=>{try{if(!e.target.dataset.economy)return;let node=economy;const parts=e.target.dataset.economy.split('.'),key=parts.pop();for(const p of parts)node=node[p];
 node[key]=typeof node[key]==='boolean'?e.target.value==='true':typeof node[key]==='number'?integer(e.target.value,0,Number.MAX_SAFE_INTEGER,key):e.target.value;syncPlan();
 }catch(err){alertError(err);}});
function renderFactors(){
 $('factors').innerHTML=draft.factors.map((f,i)=>'<span class="factor-chip">'+esc(catalog.factorLabels[f.path]??f.path)+' · '+esc(JSON.stringify(f.values??f.range))+'<button data-remove-factor="'+i+'" aria-label="Убрать '+esc(catalog.factorLabels[f.path]??f.path)+'">×</button></span>').join('');
}
$('factors').addEventListener('click',e=>{if(e.target.dataset.removeFactor!==undefined){draft.factors.splice(Number(e.target.dataset.removeFactor),1);renderFactors();syncPlan();}});
bind('add-factor',()=>{
 const path=$('factor').value,kind=typeof catalog.factors[path][0];
 const values=$('factor-values').value.split(',').map(s=>{s=s.trim();if(kind==='boolean'){if(!['true','false'].includes(s))throw Error('Используйте true или false');return s==='true';}return kind==='number'?integer(s,0,1000000000,'Фактор'):s;});
 draft.factors=draft.factors.filter(f=>f.path!==path);draft.factors.push({path,values});renderFactors();syncPlan();
});
['experiment-name','ticks','seeds','repetitions','timeout','mode'].forEach(id=>$(id).addEventListener('input',syncPlan));
$('map').addEventListener('change',()=>{const p=formPlan();delete p.participants;delete p.economyData;setPlan(p).catch(alertError);});
$('preset').addEventListener('change',()=>{const p=catalog.presets.find(p=>p.id===$('preset').value);$('preset-description').textContent=p.description;setPlan(p.plan).catch(alertError);});
bind('apply-json',()=>setPlan(parsePlan($('plan').value)));
async function queue(run){const plan=formPlan();$('run').disabled=$('queue').disabled=true;try{
 const r=await api('/api/queue',plan);if(run)await api('/api/start',{});await refresh();selectedId=r.id;runsPage=0;await showPage('detail');
 }finally{$('run').disabled=$('queue').disabled=false;}}
bind('run',()=>queue(true));bind('queue',()=>queue(false));
function renderCatalog(){
 const needle=$('search').value.toLowerCase(),archive=$('show-archive').checked;
 const rows=state.series.filter(s=>!!s.archived===archive&&((s.name??s.plan.id)+' '+s.id).toLowerCase().includes(needle)).slice().reverse();
 $('catalog').innerHTML=rows.length?rows.map(s=>{
 const completed=s.jobs.filter(j=>j.status==='succeeded').length,active=s.jobs.find(j=>j.status==='running'),status=active?'running':completed===s.jobs.length?'succeeded':s.jobs.find(j=>['failed','cancelled','interrupted'].includes(j.status))?.status??'queued';
 const map=catalog.maps.find(m=>m.path===s.plan.scenario)?.label??s.plan.scenario;
 return '<article class="card"><div class="card-top"><input type="checkbox" name="series" value="'+esc(s.id)+'" '+(selected.has(s.id)?'checked':'')+' aria-label="Выбрать '+esc(s.name??s.plan.id)+'"><span class="pill '+status+'">'+labels[status]+'</span></div><h2><button data-open="'+esc(s.id)+'">'+esc(s.name??s.plan.id)+'</button></h2><p>'+esc(map)+'</p><p>'+s.plan.ticks+' тиков · '+s.plan.seeds.length+' seed · '+esc(s.plan.mode)+'</p><progress max="'+s.jobs.length+'" value="'+completed+'"></progress><p>'+bytes(s.bytes??0)+' · Готово: '+completed+' / '+s.jobs.length+(active?.progress?' · шаг '+active.progress.tick+' / '+active.progress.target:'')+'</p></article>';
 }).join(''):'<div class="empty"><h2>'+(archive?'Архив пуст':'Первое исследование начинается здесь')+'</h2><p class="muted">Выберите готовую проверку или настройте свой опыт.</p><button class="primary" data-page="new">Создать исследование</button></div>';
}
$('catalog').addEventListener('change',e=>{if(e.target.name==='series'){if(e.target.checked)selected.add(e.target.value);else selected.delete(e.target.value);}});
$('catalog').addEventListener('click',e=>{const b=e.target.closest('[data-open]');if(b){selectedId=b.dataset.open;runsPage=0;showPage('detail').catch(alertError);}});
$('search').addEventListener('input',renderCatalog);$('show-archive').addEventListener('change',renderCatalog);
async function refresh(){state=await api('/api/state');$('queue-status').textContent=state.paused?'Очередь на паузе':state.running?'Очередь работает':'Очередь завершена';if(page==='catalog')renderCatalog();if(page==='detail')renderDetail();}
function renderDetail(){
 const s=currentSeries();if(!s){showPage('catalog');return;}
 $('detail-name').textContent=s.name??s.plan.id;$('detail-id').textContent=s.id;$('archive').textContent=s.archived?'Вернуть из архива':'В архив';
 const active=s.jobs.find(j=>j.status==='running'),p=active?.progress,completed=s.jobs.filter(j=>j.status==='succeeded').length;
 $('live-progress').innerHTML='<div class="metric-grid"><div class="metric"><span>ЗАВЕРШЕНО ПРОГОНОВ</span><strong>'+completed+' / '+s.jobs.length+'</strong></div><div class="metric"><span>ТЕКУЩИЙ ШАГ</span><strong>'+(p?p.tick+' / '+p.target:'—')+'</strong></div><div class="metric"><span>СОСТОЯНИЕ</span><strong style="font-size:18px">'+esc(active?(labels[p?.status]??'Запуск runner'):'Нет активного прогона')+'</strong></div><div class="metric"><span>СКОРОСТЬ / ВРЕМЯ</span><strong style="font-size:18px">'+(p?Number(p.ticksPerSecond).toFixed(1)+' тик/с · '+Math.round(p.elapsedSeconds)+' с · '+(p.modelRequests??0)+' запр.':'—')+'</strong></div></div><progress max="100" value="'+(p?.percent??100*completed/s.jobs.length)+'"></progress>'+(p?'<p>Последний шаг: '+esc(p.lastAdvancedAt)+' · связь: '+esc(p.heartbeatAt)+'</p>':'')+(active&&!p?'<p>Runner готовит исходный мир. Если статус долго не меняется, откройте файлы и журналы попытки.</p>':'');
 const old=$('job').value,html=s.jobs.map(j=>'<option value="'+esc(j.id)+'">'+esc(j.id)+' · '+labels[j.status]+'</option>').join('');
 if($('job').innerHTML!==html){$('job').innerHTML=html;if(s.jobs.some(j=>j.id===old))$('job').value=old;else $('job').value=(active??s.jobs[0]).id;updateAttempts();}
 const jobs=s.jobs.slice(runsPage*50,runsPage*50+50);
 $('runs').innerHTML=table(['Прогон','Seed','Факторы','Состояние','Попыток','Результат'],jobs.map(j=>[
 '<button data-select-job="'+esc(j.id)+'">'+esc(j.id)+'</button>',esc(j.seed),esc(JSON.stringify(j.factors)), '<span data-status="'+j.status+'" class="pill '+j.status+'">'+labels[j.status]+'</span>',j.attempt,esc(j.error??(j.metrics?'Капитал '+money(j.metrics.capitalMinor)+' мон.':'—'))]));
 $('runs-page').textContent=(runsPage+1)+' / '+Math.max(1,Math.ceil(s.jobs.length/50));
 document.querySelectorAll('[data-control]').forEach(b=>b.disabled=!active);
}
function updateAttempts(){const job=currentJob();if(!job)return;const old=$('attempt').value;$('attempt').innerHTML=Array.from({length:job.attempt},(_,i)=>'<option>'+(i+1)+'</option>').join('');$('attempt').value=old&&Number(old)<=job.attempt?old:String(job.attempt);$('world-slider').max=job.progress?.tick??job.metrics?.ticks??job.ticks;}
$('job').addEventListener('change',()=>{ $('attempt').value='';updateAttempts();loadDetailTab().catch(alertError);});
$('attempt').addEventListener('change',()=>loadDetailTab().catch(alertError));
$('runs').addEventListener('click',e=>{const b=e.target.closest('[data-select-job]');if(b){$('job').value=b.dataset.selectJob;updateAttempts();}});
bind('runs-prev',()=>{runsPage=Math.max(0,runsPage-1);renderDetail();});bind('runs-next',()=>{runsPage=Math.min(Math.ceil(currentSeries().jobs.length/50)-1,runsPage+1);renderDetail();});
async function manage(action,extra={}){return api('/api/manage',{action,id:selectedId,...extra});}
bind('rename',async()=>{const name=prompt('Новое название',currentSeries().name??currentSeries().plan.id);if(name){await manage('rename',{name});await refresh();}});
bind('archive',async()=>{await manage(currentSeries().archived?'restore':'archive');await refresh();await showPage('catalog');});
bind('duplicate',async()=>{const r=await manage('duplicate');await setPlan(r.plan);await showPage('new');});
bind('delete',async()=>{if(confirm('Удалить опыт и все его файлы с диска?')){await manage('delete');selected.delete(selectedId);await refresh();await showPage('catalog');}});
bind('bulk-delete',async()=>{if(!selected.size)throw Error('Выберите опыты');if(confirm('Удалить выбранные опыты и все их файлы с диска?')){await api('/api/manage',{action:'delete',selected:[...selected]});selected.clear();await refresh();}});
async function download(url,name){const r=await fetch(url,{headers:{'X-Lab-Token':token}});if(!r.ok){const e=await r.json();throw Error(e.error);}const link=document.createElement('a');link.href=URL.createObjectURL(await r.blob());link.download=name;link.click();setTimeout(()=>URL.revokeObjectURL(link.href),1000);}
bind('zip',()=>download('/archives/'+encodeURIComponent(selectedId)+'.zip',selectedId+'.zip'));
bind('compare',async()=>{if(!selected.size)throw Error('Выберите опыты');const data=await api('/api/compare',{selected:[...selected]});$('comparison').innerHTML='<div class="panel"><h2>Сравнение выбранных серий</h2>'+table(['Опыт / вариант','Завершено','Оценка капитала, монеты','Занятость'],data.groups.map(g=>[esc(g.series+' / '+g.arm),g.complete,g.metrics.capitalMinor?money(g.metrics.capitalMinor.median):'—',esc(g.metrics.employment?.median??'—')]))+'<p class="muted">Парных сравнений: '+data.pairedRightMinusLeft.length+'. Повторы одного seed не являются независимой выборкой.</p><pre>'+esc(JSON.stringify(data.pairedRightMinusLeft,null,2))+'</pre></div>';});
async function loadDetailTab(){
 stopPlayback();if(!$('attempt').value)return;
 if(detailTab==='world')await openWorld();
 if(detailTab==='files')await loadFiles();
 if(detailTab==='journal'){journalCursor=0;await loadJournal();}
 if(detailTab==='analysis'){renderMetrics();await chart();}
}
function renderMetrics(){const m=currentJob()?.metrics;if(!m){$('metrics').textContent='Результаты появятся после успешного завершения.';return;}
 $('metrics').innerHTML='<div class="metric-grid">'+[['Оценка капитала',money(m.capitalMinor)+' мон.'],['Стоимость выпуска',money(m.outputBaseMinor)+' мон.'],['Занятость',m.employment+' чел.'],['Просрочки',m.lateDeliveries+m.latePickups]].map(([a,b])=>'<div class="metric"><span>'+a+'</span><strong>'+b+'</strong></div>').join('')+'</div><p>'+esc(m.profitDefinition)+'</p>';
}
async function openWorld(){
 const tick=integer($('world-tick').value,0,currentJob()?.ticks??10000,'Шаг');
 const data=await api(query('/api/world',{...attemptParams(),tick}));$('world-slider').value=tick;$('frame-hash').textContent=data.stateHash.slice(0,20);
 const canvas=$('world-canvas'),ctx=canvas.getContext('2d'),f=data.frame,size=Math.min((canvas.width-60)/f.mapWidth,(canvas.height-60)/f.mapHeight),ox=(canvas.width-size*f.mapWidth)/2,oy=(canvas.height-size*f.mapHeight)/2;
 ctx.clearRect(0,0,canvas.width,canvas.height);ctx.fillStyle='#1c1e1d';ctx.fillRect(0,0,canvas.width,canvas.height);
 for(let x=0;x<f.mapWidth;x++)for(let z=0;z<f.mapHeight;z++){ctx.fillStyle='#26312b';ctx.fillRect(ox+x*size,oy+z*size,size-1,size-1);}
 const colors={corridor:'#444744','plot-free':'#72654b','plot-own':'#aa9361','plot-foreign':'#9c8260','plot-reserved':'#80704a','shop-plot':'#6f5985',capital:'#78a69f',resource:'#5b8559','resource-depleted':'#424c3c',blocked:'#161918',production:'#b7a078',warehouse:'#98896e',shop:'#a48aba',equipment:'#9eaa87','truck-home':'#718487'};
 f.markers.forEach(m=>{ctx.fillStyle=colors[m.kind]??'#7e8a76';ctx.fillRect(ox+m.x*size+1,oy+m.z*size+1,size-3,size-3);if(m.kind.startsWith('resource')){ctx.fillStyle='#e4eadf';ctx.font=Math.max(9,size*.32)+'px sans-serif';ctx.fillText({wood:'Л',grain:'З',milk:'М',beet:'С'}[m.label]??m.label.slice(0,1),ox+m.x*size+size*.28,oy+m.z*size+size*.64);}});
 f.actors.forEach((a,i)=>{ctx.fillStyle=['#edf0e5','#dca789','#b5a6d8','#8eb5d5'][i%4];ctx.beginPath();ctx.arc(ox+(a.x+.5)*size,oy+(a.z+.5)*size,size*.29,0,Math.PI*2);ctx.fill();ctx.fillStyle='#18201a';ctx.font='bold '+Math.max(9,size*.37)+'px sans-serif';ctx.textAlign='center';ctx.fillText(a.id,ox+(a.x+.5)*size,oy+(a.z+.63)*size);ctx.textAlign='left';});
 f.trips.forEach(t=>{ctx.fillStyle=t.loaded?'#d4b371':'#8ca4ac';ctx.fillRect(ox+(t.x+.15)*size,oy+(t.z+.15)*size,size*.35,size*.25);});
 $('world-actors').innerHTML=table(['Участник','Клетка','Деньги, монеты','Занят','Рюкзак'],data.actors.map(a=>[a.id.value,a.position.x+', '+a.position.z,money(a.cashMinor),a.busy?'Да':'Нет',esc(a.inventory.stock.map(s=>s.itemId+': '+s.quantity).join(', ')||'Пусто')]));
}
function stopPlayback(){if(worldPlayback){clearInterval(worldPlayback);worldPlayback=null;}$('play-frames').textContent='Воспроизвести';}
bind('open-tick',openWorld);bind('step-back',()=>{ $('world-tick').value=Math.max(0,Number($('world-tick').value)-1);return openWorld();});
bind('step-next',()=>{ $('world-tick').value=Math.min(Number($('world-slider').max),Number($('world-tick').value)+1);return openWorld();});
$('world-slider').addEventListener('change',()=>{$('world-tick').value=$('world-slider').value;openWorld().catch(alertError);});
bind('play-frames',()=>{if(worldPlayback){stopPlayback();return;}$('play-frames').textContent='Пауза просмотра';let busy=false;worldPlayback=setInterval(async()=>{if(busy)return;busy=true;try{if(Number($('world-tick').value)>=Number($('world-slider').max)){stopPlayback();return;}$('world-tick').value=Number($('world-tick').value)+1;await openWorld();}catch(e){stopPlayback();alertError(e);}finally{busy=false;}},350);});
async function loadFiles(){
 const data=await api(query('/api/describe',{id:selectedId})),base=prefix();
 $('file-list').innerHTML=data.files.filter(f=>!f.path.startsWith('runs/')||f.path.startsWith(base)).map(f=>'<button data-file="'+esc(f.path)+'">'+esc(f.path)+' <span class="muted">'+bytes(f.bytes)+'</span></button>').join('');
}
$('file-list').addEventListener('click',e=>{const b=e.target.closest('[data-file]');if(b){filePath=b.dataset.file;fileCursor=0;previewFile().catch(alertError);}});
async function previewFile(){const r=await api(query('/api/preview',{id:selectedId,path:filePath,cursor:fileCursor}));$('file-name').textContent=filePath;$('file-preview').textContent=r.text;fileCursor=r.nextCursor;$('file-more').hidden=!r.hasMore;$('file-download').hidden=false;}
bind('file-more',previewFile);bind('file-download',()=>download('/files/'+selectedId+'/'+filePath,filePath.split('/').pop()));
async function loadJournal(){
 const r=await api(query('/api/journal',{...attemptParams(),kind:$('journal-kind').value,actor:$('journal-actor').value,search:$('journal-search').value,important:$('journal-important').checked,cursor:journalCursor}));
 $('journal').innerHTML=r.rows.length?r.rows.map(row=>'<details class="journal-row"><summary>Шаг '+esc(row.tick)+' · участник '+esc(row.actorId?.value??row.actorId??row.actor?.value??'—')+' · '+esc(row.type??row.decision?.goal??row.reply?.outcome??'Запись')+'</summary><pre>'+esc(JSON.stringify(row,null,2))+'</pre></details>').join(''):'<p class="muted">На этой странице записей нет. Для модели сначала нужен реальный запрос.</p>';journalCursor=r.nextCursor;$('journal-more').hidden=!r.hasMore;
}
bind('journal-load',()=>{journalCursor=0;return loadJournal();});bind('journal-more',loadJournal);
async function chart(){
 const d=await api(query('/api/chart',attemptParams())),c=$('chart'),ctx=c.getContext('2d'),metric=$('chart-metric').value,moneyMetric=metric.endsWith('minor');
 const actors=[...new Set(d.rows.map(r=>r.actor_id))],maxX=Math.max(1,...d.rows.map(r=>Number(r.tick))),maxY=Math.max(1,...d.rows.map(r=>Number(r[metric])/(moneyMetric?100:1)));
 ctx.clearRect(0,0,c.width,c.height);ctx.fillStyle='#1c1e1d';ctx.fillRect(0,0,c.width,c.height);ctx.font='12px sans-serif';ctx.fillStyle='#9caaa0';
 for(let i=0;i<=4;i++){const y=35+i*80;ctx.strokeStyle='#353b35';ctx.beginPath();ctx.moveTo(65,y);ctx.lineTo(c.width-25,y);ctx.stroke();ctx.fillText((maxY*(1-i/4)).toFixed(1),8,y+4);}
 const colors=['#bcd1b6','#d5a48a','#b1a3d3','#8bb4d1','#ccb96f'];
 actors.forEach((actor,i)=>{ctx.strokeStyle=colors[i%5];ctx.beginPath();d.rows.filter(r=>r.actor_id===actor).forEach((r,n)=>{const x=65+Number(r.tick)/maxX*(c.width-90),y=355-Number(r[metric])/(moneyMetric?100:1)/maxY*320;n?ctx.lineTo(x,y):ctx.moveTo(x,y);});ctx.stroke();ctx.fillStyle=colors[i%5];ctx.fillText('NPC '+actor,80+(i%7)*130,380+Math.floor(i/7)*15);});
 $('chart-note').textContent='Источник: agents.csv · '+d.sourceRows+' строк · показана каждая '+d.stride+'-я строка; исходные данные сохранены. Горизонталь — тики, 0–'+maxX+'.';
}
bind('chart-load',chart);
async function loadProfiles(){
 profiles=await api('/api/profiles');for(const p of profiles)p.value=await api(query('/api/profile',{id:p.id}));
 for(const [kind,id] of [['economy','profiles'],['model','model-profiles']])$(id).innerHTML=profiles.filter(p=>p.kind===kind).map(p=>'<article class="card"><span class="eyebrow">'+(kind==='economy'?'ЭКОНОМИКА':'OLLAMA')+'</span><h2>'+esc(p.name)+'</h2><p>'+esc(kind==='economy'?p.value.id:p.value.ollama.model)+'</p>'+(kind==='economy'?'<button data-load-profile="'+p.id+'">Использовать в новом опыте</button>':'<p>Доступен в выборе управления участником.</p>')+'</article>').join('')||'<p class="muted">Сохранённых профилей пока нет.</p>';
}
$('profiles').addEventListener('click',e=>{const id=e.target.dataset.loadProfile;if(id){economy=clone(profiles.find(p=>p.id===id).value);renderEconomy();showPage('new').catch(alertError);notice('Профиль скопирован в новый опыт');}});
bind('save-economy',async()=>{const name=prompt('Название профиля','Моя экономика');if(name){await api('/api/profiles',{kind:'economy',name,value:economy});await loadProfiles();notice('Профиль сохранён');}});
bind('scan-models',async()=>{const r=await api('/api/models',{endpoint:$('model-endpoint').value});$('installed-models').innerHTML=(r.models??[]).map(m=>'<option value="'+esc(m.name)+'"></option>').join('');$('model-status').textContent='Найдено моделей: '+(r.models?.length??0)+'. Наличие в списке ещё не подтверждает скорость inference.';});
bind('save-model',async()=>{const value={ollama:{endpoint:$('model-endpoint').value,model:$('model-tag').value,contextTokens:4096,maximumOutputTokens:512,seed:42},runtime:{queueCapacity:3,timeoutMilliseconds:integer($('model-timeout').value,1,120,'Таймаут')*1000}};
 await api('/api/profiles',{kind:'model',name:$('model-name').value,value});await loadProfiles();renderMembers();notice('Профиль модели доступен участникам нового опыта');});
bind('shutdown',async()=>{if(confirm('Завершить лабораторию после окончания активного прогона?')){await api('/api/shutdown',{});notice('Лаборатория завершает работу');clearInterval(poll);}});
async function init(){catalog=await api('/api/catalog');$('preset').innerHTML=catalog.presets.map(p=>'<option value="'+p.id+'">'+esc(p.label)+'</option>').join('');$('map').innerHTML=catalog.maps.map(m=>'<option value="'+esc(m.path)+'">'+esc(m.label)+'</option>').join('');$('factor').innerHTML=Object.keys(catalog.factors).map(k=>'<option value="'+k+'">'+esc(catalog.factorLabels[k]??k)+'</option>').join('');await loadProfiles();await setPlan(catalog.presets[0].plan);$('preset-description').textContent=catalog.presets[0].description;await refresh();}
let refreshing=false;const poll=setInterval(async()=>{if(refreshing)return;refreshing=true;try{await refresh();}catch(e){alertError(e);}finally{refreshing=false;}},1000);init().catch(alertError);
